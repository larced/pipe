namespace GraphLibrary.Rules;

/// <summary>
/// The rule-evaluation engine over <c>(graph, selection, rules)</c> (CONTEXT.md → Evaluator, ADR
/// 0005). It answers <see cref="Check{TNode,TEdge,TInstanceData}"/> — "is this selection valid, and if
/// not why not" — and <see cref="Availability{TNode,TEdge,TInstanceData}"/> — "what may I add next, and
/// why is the rest blocked". It only ever queries; it is <b>not a solver</b> — it never completes,
/// enumerates, or satisfies a selection.
/// </summary>
public static class Evaluator
{
    // Beyond this many probed occurrences of a candidate with no upper-bound breach, headroom is
    // reported as unbounded (null) rather than searched further. Each probe materializes a view over
    // that many synthetic occurrences, so the ceiling bounds the cost of the common uncapped candidate
    // (whose headroom is genuinely unbounded) to a handful of small probes — a headroom larger than this
    // is indistinguishable from "no practical limit" for a why-not preview and is reported as such.
    private const int HeadroomProbeCeiling = 1024;

    /// <summary>
    /// Evaluates every rule in <paramref name="rules"/> against <paramref name="selection"/> (over
    /// <paramref name="graph"/>) and returns the flat <b>union</b> of their violations. The selection
    /// is valid exactly when the result is empty.
    /// </summary>
    /// <remarks>
    /// Rules compose flat, conjunctive, and order-free: every rule is evaluated (no short-circuit) and
    /// its findings are concatenated with no precedence — the result is a set-union in spirit, so the
    /// order the rules are supplied in never changes <em>which</em> violations appear. The base graph
    /// is only ever read (through the view's read-only surface), so Check cannot mutate it.
    /// </remarks>
    public static IReadOnlyList<Violation> Check<TNode, TEdge, TInstanceData>(
        IReadableGraph<TNode, TEdge> graph,
        Selection<TInstanceData> selection,
        IEnumerable<IRule<TNode, TEdge, TInstanceData>> rules)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rules);

        var view = new SelectionView<TNode, TEdge, TInstanceData>(graph, selection);
        var violations = new List<Violation>();

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));
            violations.AddRange(rule.Check(view));
        }

        return violations;
    }

    /// <summary>
    /// Builds a <see cref="RuleIndex{TNode,TEdge,TInstanceData}"/> over <paramref name="rules"/>,
    /// bucketing each built-in by the prototype handles it declares (<see cref="IHandleReferencing"/>)
    /// and treating every other rule as always relevant. The index is what keeps a later Availability
    /// derivation off <c>O(candidates × allRules)</c> — it re-Checks only the rules a given candidate's
    /// prototype could affect, not the whole set (ADR 0005). It changes no verdict: a full
    /// <see cref="Check{TNode,TEdge,TInstanceData}"/> and an index-pruned evaluation report the same
    /// violations.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> or any rule is null.</exception>
    public static RuleIndex<TNode, TEdge, TInstanceData> Index<TNode, TEdge, TInstanceData>(
        IEnumerable<IRule<TNode, TEdge, TInstanceData>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new RuleIndex<TNode, TEdge, TInstanceData>(rules);
    }

    /// <summary>
    /// Derives, per <paramref name="candidates"/>, whether adding it keeps <paramref name="selection"/>
    /// valid — <see cref="Availability.Available"/> (with headroom) or <see cref="Availability.Blocked"/>
    /// (with reasons) — the "what may I add next, and why is the rest blocked" answer (CONTEXT.md →
    /// Availability, ADR 0005). The result is one <see cref="Availability"/> per candidate, in the order
    /// supplied. It reads only through the view's read-only surface and a private snapshot, so — like
    /// <see cref="Check{TNode,TEdge,TInstanceData}"/> — it never mutates the base graph or the selection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Availability is <b>engine-derived</b>, not per-rule-implemented: for each candidate the engine
    /// simulates the add (a synthetic occurrence layered over a snapshot of the selection), re-runs the
    /// rules, and blocks only on <em>newly-caused</em> upper-bound / exclusion breaches — so a "why-not"
    /// preview works for arbitrary custom rules with zero extra author code. A breach is attributed by
    /// violation <b>identity, not count</b>: the newly-caused set is a multiset difference of the
    /// before/after violations, so pushing an already-full cardinality further — whose count-bearing
    /// message differs from the milder breach — registers as a fresh breach rather than being masked by
    /// the pre-existing one.
    /// </para>
    /// <para>
    /// It stays off <c>O(candidates × allRules)</c> by re-Checking only the rules a candidate's prototype
    /// could affect (<see cref="RuleIndex{TNode,TEdge,TInstanceData}.RulesReferencing"/>); a rule the
    /// index cannot pin to a handle is always re-checked, so pruning never changes a verdict. The
    /// derivation is unioned with any <see cref="Gate{TNode,TEdge,TInstanceData}"/> a rule opted into via
    /// <see cref="IGatingRule{TNode,TEdge,TInstanceData}"/>: a candidate is blocked if adding it breaches
    /// an upper bound <em>or</em> a gate's precondition over the current selection is unmet
    /// (<c>Availability = derived-blocks ∪ gate-failures</c>).
    /// </para>
    /// <para>
    /// The simulated occurrence carries <see langword="default"/> instance data, because availability is
    /// derived from counts, regions and topology; a rule whose verdict depends on a specific candidate's
    /// per-occurrence data is checked with <see cref="Check{TNode,TEdge,TInstanceData}"/> after a real
    /// <see cref="Selection{TInstanceData}.Add"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument, or any rule, is null.</exception>
    /// <exception cref="ArgumentException">
    /// A candidate names the <see langword="default"/> (label-less) region — no instance can carry it, so
    /// asking whether one may be added there is a programmer bug caught here.
    /// </exception>
    public static IReadOnlyList<Availability> Availability<TNode, TEdge, TInstanceData>(
        IReadableGraph<TNode, TEdge> graph,
        Selection<TInstanceData> selection,
        IEnumerable<IRule<TNode, TEdge, TInstanceData>> rules,
        IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(candidates);

        var ruleList = new List<IRule<TNode, TEdge, TInstanceData>>();
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));
            ruleList.Add(rule);
        }

        var index = new RuleIndex<TNode, TEdge, TInstanceData>(ruleList);
        var gatesByHandle = CollectGates(ruleList);

        // One snapshot of the current selection, reused as the baseline and as the base every candidate
        // probe layers its synthetic occurrence(s) over — nothing here touches the live selection.
        var currentInstances = selection.Instances;
        var currentView = new SelectionView<TNode, TEdge, TInstanceData>(graph, currentInstances);

        var results = new List<Availability>();
        foreach (var candidate in candidates)
        {
            results.Add(EvaluateCandidate(graph, currentInstances, currentView, index, gatesByHandle, candidate));
        }

        return results;
    }

    private static Dictionary<NodeHandle, List<Gate<TNode, TEdge, TInstanceData>>> CollectGates<TNode, TEdge, TInstanceData>(
        IEnumerable<IRule<TNode, TEdge, TInstanceData>> rules)
    {
        var gatesByHandle = new Dictionary<NodeHandle, List<Gate<TNode, TEdge, TInstanceData>>>();
        foreach (var rule in rules)
        {
            if (rule is not IGatingRule<TNode, TEdge, TInstanceData> gating)
            {
                continue;
            }

            foreach (var gate in gating.Gates)
            {
                ArgumentNullException.ThrowIfNull(gate, nameof(rules));
                if (!gatesByHandle.TryGetValue(gate.Gated, out var bucket))
                {
                    bucket = [];
                    gatesByHandle[gate.Gated] = bucket;
                }

                bucket.Add(gate);
            }
        }

        return gatesByHandle;
    }

    private static Availability EvaluateCandidate<TNode, TEdge, TInstanceData>(
        IReadableGraph<TNode, TEdge> graph,
        IReadOnlyCollection<Instance<TInstanceData>> currentInstances,
        SelectionView<TNode, TEdge, TInstanceData> currentView,
        RuleIndex<TNode, TEdge, TInstanceData> index,
        Dictionary<NodeHandle, List<Gate<TNode, TEdge, TInstanceData>>> gatesByHandle,
        Candidate candidate)
    {
        if (candidate.Region == default)
        {
            throw new ArgumentException(
                "A candidate must name a region created from a label; the default Region is not usable.",
                nameof(candidate));
        }

        // Gate failures: a gate is evaluated against the CURRENT selection (before the add) — it asks
        // "is the ground ready for this node yet", the strict half of the answer.
        var gateFailures = new List<GateFailure>();
        if (gatesByHandle.TryGetValue(candidate.Prototype, out var gates))
        {
            foreach (var gate in gates)
            {
                if (!gate.IsSatisfied(currentView))
                {
                    gateFailures.Add(new GateFailure(gate.Gated, gate.Requirement));
                }
            }
        }

        // Derived blocks: only rules referencing this prototype can change verdict when it is added, so
        // re-Check just those (plus the always-relevant ones the index cannot prune) — before vs after.
        var relevant = index.RulesReferencing(candidate.Prototype);
        var before = UpperBounds(CheckView(currentView, relevant));
        var after = UpperBounds(CheckView(ViewWith(graph, currentInstances, candidate, 1), relevant));
        var breaches = NewlyCaused(before, after);

        if (breaches.Count > 0 || gateFailures.Count > 0)
        {
            return new Availability.Blocked(candidate, breaches, gateFailures);
        }

        var headroom = Headroom(graph, currentInstances, candidate, relevant, before);
        return new Availability.Available(candidate, headroom);
    }

    // A view over the current instances plus <paramref name="copies"/> synthetic occurrences of the
    // candidate. Synthetic ids carry a stamp (0) that no real selection ever mints and distinct values,
    // so they never collide with a real instance's id or with each other.
    private static SelectionView<TNode, TEdge, TInstanceData> ViewWith<TNode, TEdge, TInstanceData>(
        IReadableGraph<TNode, TEdge> graph,
        IReadOnlyCollection<Instance<TInstanceData>> currentInstances,
        Candidate candidate,
        int copies)
    {
        var instances = new List<Instance<TInstanceData>>(currentInstances);
        for (var k = 1; k <= copies; k++)
        {
            instances.Add(new Instance<TInstanceData>(
                new InstanceId(-k, 0), candidate.Prototype, candidate.Region, default!));
        }

        return new SelectionView<TNode, TEdge, TInstanceData>(graph, instances);
    }

    // How many occurrences of an available candidate can still be added before an upper bound would
    // block the next one (>= 1), or null when nothing constrains it. Found by exponential search for the
    // first breaching count then a binary search for the boundary; assumes upper-bound rules are
    // monotonic in count (every built-in is).
    private static int? Headroom<TNode, TEdge, TInstanceData>(
        IReadableGraph<TNode, TEdge> graph,
        IReadOnlyCollection<Instance<TInstanceData>> currentInstances,
        Candidate candidate,
        IReadOnlyList<IRule<TNode, TEdge, TInstanceData>> relevant,
        IReadOnlyList<Violation> before)
    {
        bool Breaches(int copies) => NewlyCaused(
            before,
            UpperBounds(CheckView(ViewWith(graph, currentInstances, candidate, copies), relevant))).Count > 0;

        // Adding one copy is known not to breach (the candidate is available), so headroom is at least 1.
        var lastOk = 1;
        var probe = 2;
        while (probe <= HeadroomProbeCeiling && !Breaches(probe))
        {
            lastOk = probe;
            probe *= 2;
        }

        if (probe > HeadroomProbeCeiling)
        {
            return null;
        }

        // Boundary lies in (lastOk, probe]: lastOk does not breach, probe does.
        var lo = lastOk;
        var hi = probe;
        while (hi - lo > 1)
        {
            var mid = lo + ((hi - lo) / 2);
            if (Breaches(mid))
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return lo;
    }

    private static IReadOnlyList<Violation> CheckView<TNode, TEdge, TInstanceData>(
        SelectionView<TNode, TEdge, TInstanceData> view,
        IEnumerable<IRule<TNode, TEdge, TInstanceData>> rules)
    {
        var violations = new List<Violation>();
        foreach (var rule in rules)
        {
            violations.AddRange(rule.Check(view));
        }

        return violations;
    }

    private static List<Violation> UpperBounds(IReadOnlyList<Violation> violations)
    {
        var result = new List<Violation>();
        foreach (var violation in violations)
        {
            if (violation.Kind == ViolationKind.UpperBound)
            {
                result.Add(violation);
            }
        }

        return result;
    }

    // The upper-bound violations present after the add that are not accounted for before it, as a
    // multiset difference (after minus before). Keyed on the Violation value (identity, not count), so a
    // worsened cardinality — whose count-bearing message differs — surfaces as newly caused.
    private static List<Violation> NewlyCaused(IReadOnlyList<Violation> before, IReadOnlyList<Violation> after)
    {
        var remaining = new Dictionary<Violation, int>();
        foreach (var violation in before)
        {
            remaining[violation] = remaining.GetValueOrDefault(violation) + 1;
        }

        var newly = new List<Violation>();
        foreach (var violation in after)
        {
            if (remaining.TryGetValue(violation, out var count) && count > 0)
            {
                remaining[violation] = count - 1;
            }
            else
            {
                newly.Add(violation);
            }
        }

        return newly;
    }
}
