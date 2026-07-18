using GraphLibrary;
using GraphLibrary.Rules;

namespace GraphLibrary.Tests;

// Seam 3 — Rule evaluation, ticket 16 (#27): the Availability oracle + opt-in Gating. Proves
// Availability is engine-derived (simulate the add, re-Check, block on newly-caused upper-bound /
// exclusion breaches) so it works for arbitrary custom rules with no per-rule availability code; that a
// blocked candidate is attributed by violation identity (a fresh breach on an already-full cardinality);
// Available/headroom vs Blocked/reasons; the default gentle posture vs opt-in Gating; the union of
// derived-blocks and gate-failures; and that a custom rule opting into handle-indexing never changes a
// verdict. Covers spec stories 58-63, 75. Every assertion drives only the public Rules API over a real
// base Graph, so the "derive over an overlay that never mutates the base graph or selection" contract
// holds (ADR 0005).
public class AvailabilityTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);
    private sealed record InstanceData(string Note);

    private static readonly Region FrontAxle = new("FrontAxle");
    private static readonly Region RearAxle = new("RearAxle");
    private static readonly Region Cabin = new("Cabin");

    private static (Graph<Product, Dependency> graph, NodeHandle wheel, NodeHandle engine, NodeHandle seat) Fixture()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var engine = graph.AddNode(new Product("engine"));
        var seat = graph.AddNode(new Product("seat"));
        return (graph, wheel, engine, seat);
    }

    private static IReadOnlyList<Availability> Availability(
        Graph<Product, Dependency> graph,
        Selection<InstanceData> selection,
        IEnumerable<Candidate> candidates,
        params IRule<Product, Dependency, InstanceData>[] rules) =>
        Evaluator.Availability(graph, selection, rules, candidates);

    private static Availability Single(
        Graph<Product, Dependency> graph,
        Selection<InstanceData> selection,
        Candidate candidate,
        params IRule<Product, Dependency, InstanceData>[] rules) =>
        Assert.Single(Availability(graph, selection, [candidate], rules));

    private static Cardinality<Product, Dependency, InstanceData> Card(NodeHandle prototype, int min, int max) =>
        new(Scope.Global, prototype, min, max);

    // --- Available (with headroom) ---

    [Fact]
    public void Available_ReportsHeadroomToTheUpperBound()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("one"));    // 1 of a [0,3] cap

        var result = Single(graph, selection, new Candidate(wheel, FrontAxle), Card(wheel, 0, 3));

        var available = Assert.IsType<Availability.Available>(result);
        Assert.Equal(2, available.Headroom);                         // 2 more before the 4th breaches
    }

    [Fact]
    public void Available_WithNoUpperBoundConstrainingIt_ReportsUnboundedHeadroom()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();

        // A lower-bound-only rule never caps the candidate, so headroom is unbounded (null).
        var result = Single(graph, selection, new Candidate(wheel, FrontAxle), Card(wheel, 1, int.MaxValue));

        var available = Assert.IsType<Availability.Available>(result);
        Assert.Null(available.Headroom);
    }

    [Fact]
    public void Available_WhenNoRuleMentionsTheCandidate()
    {
        var (graph, wheel, _, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));

        // Only a wheel rule exists; a seat candidate is unconstrained and available with no cap.
        var result = Single(graph, selection, new Candidate(seat, Cabin), Card(wheel, 0, 4));

        var available = Assert.IsType<Availability.Available>(result);
        Assert.Null(available.Headroom);
    }

    // --- Blocked (with reasons), engine-derived from a built-in ---

    [Fact]
    public void Blocked_WhenAddingWouldBreachAnUpperBound()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(wheel, RearAxle, new InstanceData("b"));       // at the [0,2] cap

        var result = Single(graph, selection, new Candidate(wheel, Cabin), Card(wheel, 0, 2));

        var blocked = Assert.IsType<Availability.Blocked>(result);
        var breach = Assert.Single(blocked.Breaches);
        Assert.Equal(ViolationKind.UpperBound, breach.Kind);
        Assert.Empty(blocked.GateFailures);
    }

    [Fact]
    public void Blocked_ByAConflictGroup_WhenAddingADistinctAlternative()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));      // one alternative already present

        var conflict = new ConflictGroup<Product, Dependency, InstanceData>(wheel, engine);
        var result = Single(graph, selection, new Candidate(engine, Cabin), conflict);

        var blocked = Assert.IsType<Availability.Blocked>(result);
        Assert.Equal(ViolationKind.UpperBound, Assert.Single(blocked.Breaches).Kind);
    }

    [Fact]
    public void Available_WhenAddingAnotherOfTheSameConflictMember()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));

        // A second wheel is not a distinct alternative, so the conflict group does not block it.
        var conflict = new ConflictGroup<Product, Dependency, InstanceData>(wheel, engine);
        var result = Single(graph, selection, new Candidate(wheel, RearAxle), conflict);

        Assert.IsType<Availability.Available>(result);
    }

    // --- Story 59: attributed by violation identity, not count (fresh breach on an already-full cap) ---

    [Fact]
    public void Blocked_PushingAnAlreadyFullCardinalityFurther_IsAFreshBreach()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(wheel, RearAxle, new InstanceData("b"));
        selection.Add(wheel, Cabin, new InstanceData("c"));          // already over the [0,2] cap

        // The rule is already breached; adding another wheel worsens it and must register as blocked —
        // attribution is by violation identity (the count-bearing breach differs), not by counting the
        // number of violations before vs after.
        var result = Single(graph, selection, new Candidate(wheel, FrontAxle), Card(wheel, 0, 2));

        var blocked = Assert.IsType<Availability.Blocked>(result);
        var breach = Assert.Single(blocked.Breaches);
        Assert.Equal(ViolationKind.UpperBound, breach.Kind);
        Assert.Contains("found 4", breach.Message);                  // the fresh, worsened breach
    }

    [Fact]
    public void Available_ForAnUnrelatedCandidate_EvenWhenTheSelectionIsAlreadyInvalid()
    {
        var (graph, wheel, _, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(wheel, RearAxle, new InstanceData("b"));       // wheel cap [0,1] already breached

        // The seat candidate causes no NEW breach, so it is available despite the standing wheel breach.
        var result = Single(graph, selection, new Candidate(seat, Cabin), Card(wheel, 0, 1));

        Assert.IsType<Availability.Available>(result);
    }

    // --- Region-scoped availability: the candidate's region is part of the question ---

    [Fact]
    public void RegionScoped_BlocksInTheFullRegionButNotAnotherRegion()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(wheel, FrontAxle, new InstanceData("b"));      // FrontAxle at its [0,2] cap

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Region(FrontAxle), wheel, 0, 2);
        var results = Availability(
            graph, selection,
            [new Candidate(wheel, FrontAxle), new Candidate(wheel, RearAxle)],
            rule);

        Assert.IsType<Availability.Blocked>(results[0]);             // FrontAxle is full
        Assert.IsType<Availability.Available>(results[1]);           // RearAxle has room
    }

    // --- Engine-derived over an arbitrary custom rule, with no per-rule availability code ---

    [Fact]
    public void Blocked_ByACustomRule_WithNoAvailabilityCode()
    {
        var (graph, wheel, engine, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(engine, RearAxle, new InstanceData("b"));     // 2 active regions (the cap)

        // A custom "at most 2 active regions" upper-bound rule; adding a seat into a third region would
        // push active regions to 3 — engine-derived block with no availability-specific code.
        var result = Single(graph, selection, new Candidate(seat, Cabin), new AtMostActiveRegions(2));

        var blocked = Assert.IsType<Availability.Blocked>(result);
        Assert.Equal(ViolationKind.UpperBound, Assert.Single(blocked.Breaches).Kind);
    }

    [Fact]
    public void Available_WhenACustomRuleAddsOnlyToAnAlreadyActiveRegion()
    {
        var (graph, wheel, engine, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(engine, RearAxle, new InstanceData("b"));     // 2 active regions (the cap)

        // Adding another instance into an existing region keeps the active-region count at 2 — available.
        var result = Single(graph, selection, new Candidate(seat, FrontAxle), new AtMostActiveRegions(2));

        Assert.IsType<Availability.Available>(result);
    }

    // --- Default posture is gentle/eventual: a lower-bound never blocks an add (story 61) ---

    [Fact]
    public void Available_EvenWhenAddingLeavesAStandingLowerBoundShortfall()
    {
        var (graph, wheel, engine, _) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("requires"));    // engine requires wheel
        var selection = new Selection<InstanceData>();

        // Adding the engine leaves a requires-shortfall (wheel absent), but the gentle default lets you
        // add it freely — the selection is invalid-until-satisfied, not blocked.
        var rule = new RequiresEdge<Product, Dependency, InstanceData>(d => d.Kind == "requires");
        var result = Single(graph, selection, new Candidate(engine, Cabin), rule);

        Assert.IsType<Availability.Available>(result);
    }

    // --- Opt-in Gating: strict, evaluated against the current selection (stories 62-63) ---

    [Fact]
    public void Gated_BlocksUntilThePrerequisiteIsPresent()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();

        var gate = new GatedBy<Product, Dependency, InstanceData>(wheel, engine);   // wheel gated by engine

        var blockedResult = Single(graph, selection, new Candidate(wheel, FrontAxle), gate);
        var blocked = Assert.IsType<Availability.Blocked>(blockedResult);
        Assert.Empty(blocked.Breaches);                              // purely a gate failure, not a breach
        var failure = Assert.Single(blocked.GateFailures);
        Assert.Equal(wheel, failure.Gated);

        selection.Add(engine, Cabin, new InstanceData("motor"));     // prerequisite now present
        Assert.IsType<Availability.Available>(Single(graph, selection, new Candidate(wheel, FrontAxle), gate));
    }

    [Fact]
    public void Gated_DoesNotAffectCheckValidity()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));      // wheel present without its gate met

        // Gating governs when a node may be added, not whether the selection is valid — Check is clean.
        var gate = new GatedBy<Product, Dependency, InstanceData>(wheel, engine);
        Assert.Empty(Evaluator.Check(graph, selection, new[] { gate }));
    }

    [Fact]
    public void Gated_ReportsOneFailurePerMissingPrerequisite()
    {
        var (graph, wheel, engine, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));     // one of two prerequisites present

        var gate = new GatedBy<Product, Dependency, InstanceData>(wheel, engine, seat);
        var blocked = Assert.IsType<Availability.Blocked>(
            Single(graph, selection, new Candidate(wheel, FrontAxle), gate));

        Assert.Single(blocked.GateFailures);                         // only the missing seat prerequisite
    }

    // --- Availability = derived-blocks ∪ gate-failures: both mechanisms surface in one answer ---

    [Fact]
    public void Blocked_SurfacesDerivedBreachesAndGateFailuresTogether()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));      // wheel at its [0,1] cap

        var result = Single(
            graph, selection, new Candidate(wheel, RearAxle),
            Card(wheel, 0, 1),                                       // adding a wheel breaches upper bound
            new GatedBy<Product, Dependency, InstanceData>(wheel, engine));  // and wheel is gated by engine

        var blocked = Assert.IsType<Availability.Blocked>(result);
        Assert.Equal(ViolationKind.UpperBound, Assert.Single(blocked.Breaches).Kind);
        Assert.Single(blocked.GateFailures);
    }

    [Fact]
    public void CustomGatingRule_ParticipatesThroughTheSameInterface()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();

        // A custom rule that gates the wheel until at least two instances exist in the selection.
        var rule = new GateUntilTwoInstances(wheel);
        var blocked = Assert.IsType<Availability.Blocked>(
            Single(graph, selection, new Candidate(wheel, FrontAxle), rule));
        Assert.Single(blocked.GateFailures);

        selection.Add(wheel, RearAxle, new InstanceData("a"));
        selection.Add(wheel, Cabin, new InstanceData("b"));          // now two instances exist
        Assert.IsType<Availability.Available>(Single(graph, selection, new Candidate(wheel, FrontAxle), rule));
    }

    // --- Story 75: a custom rule opting into handle-indexing never changes a verdict ---

    [Fact]
    public void CustomRule_HandleIndexingOptIn_DoesNotChangeVerdicts()
    {
        var (graph, wheel, _, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));      // wheel at the custom [0,1] cap

        Candidate[] candidates = [new Candidate(wheel, RearAxle), new Candidate(seat, Cabin)];

        // The same cap, one declaring its handle (indexed opt-in), one not (always relevant, unindexed).
        var indexed = Availability(graph, selection, candidates, new IndexedWheelCap(wheel));
        var unindexed = Availability(graph, selection, candidates, new UnindexedWheelCap(wheel));

        // Identical verdicts: the wheel candidate is blocked, the seat candidate is available, both ways.
        Assert.IsType<Availability.Blocked>(indexed[0]);
        Assert.IsType<Availability.Available>(indexed[1]);
        Assert.IsType<Availability.Blocked>(unindexed[0]);
        Assert.IsType<Availability.Available>(unindexed[1]);
    }

    // --- Purity, ordering, and argument guards ---

    [Fact]
    public void Availability_MutatesNeitherTheSelectionNorTheBaseGraph()
    {
        var (graph, wheel, engine, _) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("requires"));
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));

        var nodesBefore = graph.Nodes.ToArray();
        var edgesBefore = graph.Edges.ToArray();
        var countBefore = selection.Count;

        _ = Availability(
            graph, selection,
            [new Candidate(wheel, RearAxle), new Candidate(engine, Cabin)],
            Card(wheel, 0, 1),
            new GatedBy<Product, Dependency, InstanceData>(engine, wheel));

        Assert.Equal(nodesBefore, graph.Nodes);
        Assert.Equal(edgesBefore, graph.Edges);
        Assert.Equal(countBefore, selection.Count);
    }

    [Fact]
    public void Availability_ReturnsOneResultPerCandidateInOrder()
    {
        var (graph, wheel, engine, seat) = Fixture();
        var selection = new Selection<InstanceData>();

        Candidate[] candidates = [new Candidate(wheel, FrontAxle), new Candidate(engine, Cabin), new Candidate(seat, Cabin)];
        var results = Availability(graph, selection, candidates, Card(wheel, 0, 4));

        Assert.Equal(3, results.Count);
        Assert.Equal(candidates[0], results[0].Candidate);
        Assert.Equal(candidates[1], results[1].Candidate);
        Assert.Equal(candidates[2], results[2].Candidate);
    }

    [Fact]
    public void Availability_EmptyCandidates_ReturnsEmpty()
    {
        var (graph, wheel, _, _) = Fixture();
        Assert.Empty(Availability(graph, new Selection<InstanceData>(), [], Card(wheel, 0, 4)));
    }

    [Fact]
    public void Availability_RejectsTheDefaultRegionAsACandidateRegion()
    {
        var (graph, wheel, _, _) = Fixture();
        Assert.Throws<ArgumentException>(
            () => Availability(graph, new Selection<InstanceData>(), [new Candidate(wheel, default)], Card(wheel, 0, 4)));
    }

    [Fact]
    public void Availability_RejectsNullArguments()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        var rules = new IRule<Product, Dependency, InstanceData>[] { Card(wheel, 0, 4) };
        Candidate[] candidates = [new Candidate(wheel, FrontAxle)];

        Assert.Throws<ArgumentNullException>(() => Evaluator.Availability(null!, selection, rules, candidates));
        Assert.Throws<ArgumentNullException>(() => Evaluator.Availability(graph, null!, rules, candidates));
        Assert.Throws<ArgumentNullException>(() => Evaluator.Availability(graph, selection, null!, candidates));
        Assert.Throws<ArgumentNullException>(() => Evaluator.Availability(graph, selection, rules, null!));
    }

    [Fact]
    public void GatedBy_RejectsAnEmptyPrerequisiteList()
    {
        var (_, wheel, _, _) = Fixture();
        Assert.Throws<ArgumentException>(() => new GatedBy<Product, Dependency, InstanceData>(wheel));
    }

    // --- Custom rules used above ---

    // "At most N active regions" — an upper-bound custom rule the engine derives availability over with
    // no availability-specific code (same shape as the tracer's region rules).
    private sealed class AtMostActiveRegions(int max) : IRule<Product, Dependency, InstanceData>
    {
        public IEnumerable<Violation> Check(SelectionView<Product, Dependency, InstanceData> view)
        {
            if (view.ActiveRegions.Count > max)
            {
                yield return new Violation(
                    ViolationKind.UpperBound,
                    $"At most {max} active region(s) allowed, found {view.ActiveRegions.Count}.");
            }
        }
    }

    // A custom gating rule: the gated node is unavailable until the selection holds at least two
    // instances of any kind — proves gating extends through IGatingRule, not only the built-in.
    private sealed class GateUntilTwoInstances(NodeHandle gated)
        : IRule<Product, Dependency, InstanceData>, IGatingRule<Product, Dependency, InstanceData>
    {
        public IEnumerable<Violation> Check(SelectionView<Product, Dependency, InstanceData> view) => [];

        public IEnumerable<Gate<Product, Dependency, InstanceData>> Gates =>
        [
            new Gate<Product, Dependency, InstanceData>(
                gated, view => view.GlobalCount >= 2, "requires at least two instances first"),
        ];
    }

    // The same "at most one wheel globally" upper-bound cap, one declaring its handle to opt into
    // indexing, one not — used to prove indexing never changes a verdict.
    private sealed class IndexedWheelCap(NodeHandle wheel) : WheelCap(wheel), IHandleReferencing
    {
        public IEnumerable<NodeHandle> ReferencedHandles => [Wheel];
    }

    private sealed class UnindexedWheelCap(NodeHandle wheel) : WheelCap(wheel);

    private class WheelCap(NodeHandle wheel) : IRule<Product, Dependency, InstanceData>
    {
        protected NodeHandle Wheel { get; } = wheel;

        public IEnumerable<Violation> Check(SelectionView<Product, Dependency, InstanceData> view)
        {
            if (view.Count(static _ => true, i => i.Prototype == Wheel) > 1)
            {
                yield return new Violation(ViolationKind.UpperBound, "At most one wheel allowed.");
            }
        }
    }
}
