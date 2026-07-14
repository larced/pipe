// PROTOTYPE — throwaway. Pure logic, no I/O. See README.md.
namespace RuleEvalPrototype;

// An instance = one occurrence of a prototype node, tagged into a layer.
public readonly record struct InstanceId(int Value);
public sealed record Instance(InstanceId Id, string Prototype, string Layer);

/// The projection every rule reads. Both models must be able to produce THIS,
/// regardless of how they store instances internally. If the two models ever
/// produce different views from the same actions, that's a finding.
public sealed class SelectionView
{
    private readonly IReadOnlyList<Instance> _instances;
    public SelectionView(IReadOnlyList<Instance> instances) => _instances = instances;

    public IReadOnlyList<Instance> All => _instances;
    public int CountOf(string proto) => _instances.Count(i => i.Prototype == proto);
    public int CountInLayer(string proto, string layer) =>
        _instances.Count(i => i.Prototype == proto && i.Layer == layer);
    public bool Present(string proto) => CountOf(proto) > 0;
    public IEnumerable<string> ActiveLayers => _instances.Select(i => i.Layer).Distinct();
}

// Requirement = lower-bound / "you still need X" — does NOT block adding more.
// Exclusion / UpperBound = "too many / mutually exclusive" — blocks adding.
public enum ViolationKind { Requirement, Exclusion, UpperBound }
public sealed record Violation(ViolationKind Kind, string Message);

public interface IRule
{
    string Describe();
    IEnumerable<Violation> Check(SelectionView v);
}

// --- The four motivating rule types, stated over counts (see #3) -------------

/// AND dependency: if `from` is present, `to` must be present. (global, across layers)
public sealed record AndDep(string From, string To) : IRule
{
    public string Describe() => $"AND: {From} requires {To}";
    public IEnumerable<Violation> Check(SelectionView v)
    {
        if (v.Present(From) && !v.Present(To))
            yield return new(ViolationKind.Requirement, $"{From} requires {To}, but {To} is not selected");
    }
}

/// OR dependency: if `from` is present, at least one of `oneOf` must be present.
public sealed record OrDep(string From, string[] OneOf) : IRule
{
    public string Describe() => $"OR:  {From} requires one of [{string.Join(", ", OneOf)}]";
    public IEnumerable<Violation> Check(SelectionView v)
    {
        if (v.Present(From) && !OneOf.Any(v.Present))
            yield return new(ViolationKind.Requirement,
                $"{From} requires at least one of [{string.Join(", ", OneOf)}]");
    }
}

/// Conflict group: at most one DISTINCT prototype of the group may be present. (global)
public sealed record ConflictGroup(string[] Group) : IRule
{
    public string Describe() => $"XOR: at most one of [{string.Join(", ", Group)}]";
    public IEnumerable<Violation> Check(SelectionView v)
    {
        var present = Group.Where(v.Present).ToArray();
        if (present.Length > 1)
            yield return new(ViolationKind.Exclusion,
                $"conflict: [{string.Join(", ", present)}] cannot be selected together");
    }
}

/// Instance limit: total instances of a prototype across all layers must be <= Max.
public sealed record InstanceLimit(string Prototype, int Max) : IRule
{
    public string Describe() => $"LIM: at most {Max} instance(s) of {Prototype} (all layers)";
    public IEnumerable<Violation> Check(SelectionView v)
    {
        if (v.CountOf(Prototype) > Max)
            yield return new(ViolationKind.UpperBound,
                $"{Prototype}: {v.CountOf(Prototype)} instances exceeds limit {Max}");
    }
}

/// Cardinality within a grouping key (= layer): each ACTIVE layer must hold
/// between Min and Max instances of Prototype. This is the rule that spans the
/// layer boundary — the crux of "validation across the complete graph".
public sealed record LayerCardinality(string Prototype, int Min, int Max) : IRule
{
    public string Describe() => $"CARD: each active layer needs {Min}-{Max} {Prototype}";
    public IEnumerable<Violation> Check(SelectionView v)
    {
        foreach (var layer in v.ActiveLayers)
        {
            var n = v.CountInLayer(Prototype, layer);
            if (n == 0) continue; // layer isn't using this prototype at all — fine
            if (n < Min)
                yield return new(ViolationKind.Requirement,
                    $"layer '{layer}' has {n} {Prototype}, needs at least {Min}");
            if (n > Max)
                yield return new(ViolationKind.UpperBound,
                    $"layer '{layer}' has {n} {Prototype}, exceeds {Max}");
        }
    }
}

public static class Scenario
{
    public static readonly string[] Prototypes =
        { "Engine", "Wheel", "TurboKit", "StereoA", "StereoB", "Battery" };

    public static readonly string[] Layers = { "Chassis", "FrontAxle", "RearAxle" };

    public static readonly IRule[] Rules =
    {
        new AndDep("TurboKit", "Engine"),
        new OrDep("Engine", new[] { "Battery", "TurboKit" }),
        new ConflictGroup(new[] { "StereoA", "StereoB" }),
        new InstanceLimit("Wheel", 4),
        new LayerCardinality("Wheel", 2, 2),   // each axle that has wheels needs exactly 2
    };
}

public sealed record ValidationReport(IReadOnlyList<Violation> Violations)
{
    public bool Ok => Violations.Count == 0;
}

public sealed record Avail(string Prototype, bool CanAdd, string Note);

public static class Engine_
{
    public static ValidationReport Validate(SelectionView v) =>
        new(Scenario.Rules.SelectMany(r => r.Check(v)).ToList());

    /// Live availability of adding each prototype INTO `layer`, given current state.
    /// Blocked iff adding would CAUSE an upper-bound/exclusion breach attributable to
    /// the addition. We attribute by violation IDENTITY, not count: a hard violation
    /// whose message the current state doesn't already have was caused by this add.
    /// (Comparing counts is wrong — pushing an already-over cardinality from 3->4
    /// keeps the count at one violation but is still a fresh breach.) Unsatisfied
    /// Requirements — lower bounds like "still needs Engine" — never block adding.
    public static IReadOnlyList<Avail> AvailabilityIn(SelectionView v, string layer, int nextId)
    {
        var before = Validate(v).Violations
            .Where(x => x.Kind != ViolationKind.Requirement)
            .Select(x => x.Message).ToHashSet();
        var result = new List<Avail>();
        foreach (var proto in Scenario.Prototypes)
        {
            var hypo = v.All.Append(new Instance(new InstanceId(nextId), proto, layer)).ToList();
            var caused = Validate(new SelectionView(hypo)).Violations
                .Where(x => x.Kind != ViolationKind.Requirement && !before.Contains(x.Message))
                .ToList();
            var blocking = caused.Count > 0;
            var note = blocking ? caused[^1].Message : $"in-layer: {v.CountInLayer(proto, layer)}";
            result.Add(new(proto, !blocking, note));
        }
        return result;
    }
}
