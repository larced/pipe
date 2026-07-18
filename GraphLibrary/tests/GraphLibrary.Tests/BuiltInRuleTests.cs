using GraphLibrary;
using GraphLibrary.Rules;

namespace GraphLibrary.Tests;

// Seam 3 — Rule evaluation, ticket 15 (#26): the built-in rule set + full region scope model. Proves
// Cardinality across all four scopes (named region / named set / each active region / global) folding
// to one filter-then-count primitive, InstanceLimit as a thin max-only factory, the group built-ins
// (OneOf / ConflictGroup) and the requires-edge dependency, plus the Evaluator's handle index. Covers
// spec stories 54-56, 67-72, 75. Every assertion drives only the public Rules API over a real base
// Graph, so the "Check over an overlay that never mutates the base graph" contract holds (ADR 0005).
public class BuiltInRuleTests
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

    private static IReadOnlyList<Violation> Check(
        Graph<Product, Dependency> graph,
        Selection<InstanceData> selection,
        params IRule<Product, Dependency, InstanceData>[] rules) =>
        Evaluator.Check(graph, selection, rules);

    // --- Cardinality: named-region scope counts only the named region ---

    [Fact]
    public void Cardinality_NamedRegion_CountsOnlyThatRegion()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, FrontAxle, new InstanceData("right"));
        selection.Add(wheel, RearAxle, new InstanceData("spare"));   // outside the scope

        // FrontAxle needs [3,4] wheels; it has 2, so a shortfall — the RearAxle wheel is not counted.
        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Region(FrontAxle), wheel, 3, 4);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    [Fact]
    public void Cardinality_NamedRegion_WithinBand_IsSatisfied()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, FrontAxle, new InstanceData("right"));
        selection.Add(wheel, RearAxle, new InstanceData("many1"));
        selection.Add(wheel, RearAxle, new InstanceData("many2"));
        selection.Add(wheel, RearAxle, new InstanceData("many3"));   // RearAxle over 2, but out of scope

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Region(FrontAxle), wheel, 1, 2);
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void Cardinality_NamedRegion_InactiveRegion_CountsZero()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));

        // Cabin carries no instance; a [1,2] requirement there is a lower-bound shortfall, not an error.
        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Region(Cabin), wheel, 1, 2);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    [Fact]
    public void Cardinality_RejectsTheDefaultRegionAsAScope()
    {
        Assert.Throws<ArgumentException>(() => Scope.Region(default));
    }

    // --- Cardinality: named-set scope counts across the set as one combined band ---

    [Fact]
    public void Cardinality_NamedSet_CombinesTheRegionsIntoOneBand()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("fl"));
        selection.Add(wheel, FrontAxle, new InstanceData("fr"));
        selection.Add(wheel, RearAxle, new InstanceData("rl"));
        selection.Add(wheel, RearAxle, new InstanceData("rr"));
        selection.Add(wheel, Cabin, new InstanceData("spare"));      // outside the set

        // The two axles together must hold [1,4] wheels — 4 across the set, satisfied.
        var axles = Scope.RegionSet([FrontAxle, RearAxle]);
        Assert.Empty(Check(graph, selection, new Cardinality<Product, Dependency, InstanceData>(axles, wheel, 1, 4)));

        // Tighten to [1,3]: the combined 4 now breaches upper.
        var tight = new Cardinality<Product, Dependency, InstanceData>(axles, wheel, 1, 3);
        var violation = Assert.Single(Check(graph, selection, tight));
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Fact]
    public void Cardinality_NamedSet_RejectsAnEmptySet()
    {
        Assert.Throws<ArgumentException>(() => Scope.RegionSet([]));
    }

    [Fact]
    public void Cardinality_NamedSet_IsUnaffectedByLaterMutationOfTheCallersCollection()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("fl"));
        selection.Add(wheel, RearAxle, new InstanceData("rl"));

        var regions = new List<Region> { FrontAxle };
        var scope = Scope.RegionSet(regions);
        regions.Add(RearAxle);      // must NOT change what the already-built scope counts

        // Scope still counts only FrontAxle (1 wheel); [2,4] is a shortfall despite the later Add.
        var rule = new Cardinality<Product, Dependency, InstanceData>(scope, wheel, 2, 4);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    // --- Cardinality: each-active-region fans out to one band per active region ---

    [Fact]
    public void Cardinality_EachActiveRegion_HoldsTheBandInEveryActiveRegion()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("fl"));
        selection.Add(wheel, FrontAxle, new InstanceData("fr"));     // FrontAxle: 2 wheels (ok for [2,2])
        selection.Add(wheel, RearAxle, new InstanceData("rl"));      // RearAxle: 1 wheel (short for [2,2])

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.EachActiveRegion, wheel, 2, 2);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);     // only RearAxle breaches
    }

    [Fact]
    public void Cardinality_EachActiveRegion_ReportsEveryBreachingRegion()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("only"));            // 1 -> short of [2,2]
        selection.Add(wheel, RearAxle, new InstanceData("a"));
        selection.Add(wheel, RearAxle, new InstanceData("b"));
        selection.Add(wheel, RearAxle, new InstanceData("c"));               // 3 -> over [2,2]

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.EachActiveRegion, wheel, 2, 2);
        var violations = Check(graph, selection, rule);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Kind == ViolationKind.LowerBound);
        Assert.Contains(violations, v => v.Kind == ViolationKind.UpperBound);
    }

    [Fact]
    public void Cardinality_EachActiveRegion_OnlyConsidersRegionsThatExist()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        // Only the engine region is active; a per-region wheel rule never fabricates a Cabin band.
        selection.Add(engine, Cabin, new InstanceData("motor"));

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.EachActiveRegion, wheel, 1, 4);
        // Cabin is active (has an engine) but holds 0 wheels -> one shortfall, and only one.
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    [Fact]
    public void Cardinality_EachActiveRegion_EmptySelection_ImposesNoBand()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.EachActiveRegion, wheel, 2, 2);
        Assert.Empty(Check(graph, selection, rule));
    }

    // --- InstanceLimit: a thin max-only global Cardinality, not a distinct rule type ---

    [Fact]
    public void InstanceLimit_DefaultsToTheSingleInstanceCap()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, RearAxle, new InstanceData("right"));   // second occurrence breaches [0,1]

        var rule = Cardinality<Product, Dependency, InstanceData>.InstanceLimit(wheel);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Fact]
    public void InstanceLimit_IsAGlobalMaxOnlyCardinality()
    {
        var (_, wheel, _, _) = Fixture();
        var rule = Cardinality<Product, Dependency, InstanceData>.InstanceLimit(wheel, max: 3);

        Assert.Same(Scope.Global, rule.Scope);
        Assert.Equal(0, rule.Min);
        Assert.Equal(3, rule.Max);
        Assert.Equal(wheel, rule.Prototype);
    }

    [Fact]
    public void InstanceLimit_WithExplicitMax_BreachesOnlyBeyondIt()
    {
        var (graph, wheel, _, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(wheel, FrontAxle, new InstanceData("b"));

        // max 2: two occurrences are fine, a third breaches.
        var rule = Cardinality<Product, Dependency, InstanceData>.InstanceLimit(wheel, max: 2);
        Assert.Empty(Check(graph, selection, rule));

        selection.Add(wheel, RearAxle, new InstanceData("c"));
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Fact]
    public void InstanceLimit_RejectsANegativeMax()
    {
        var (_, wheel, _, _) = Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Cardinality<Product, Dependency, InstanceData>.InstanceLimit(wheel, max: -1));
    }

    // --- OneOf: at least one of the group's prototypes must be present (OR-group) ---

    [Fact]
    public void OneOf_WithNoGroupMemberPresent_ReportsALowerBound()
    {
        var (graph, wheel, engine, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(seat, Cabin, new InstanceData("driver"));      // neither wheel nor engine

        var rule = new OneOf<Product, Dependency, InstanceData>(wheel, engine);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    [Fact]
    public void OneOf_IsSatisfiedByAnySingleMember()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));

        var rule = new OneOf<Product, Dependency, InstanceData>(wheel, engine);
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void OneOf_RejectsAnEmptyGroup()
    {
        Assert.Throws<ArgumentException>(() => new OneOf<Product, Dependency, InstanceData>());
    }

    // --- ConflictGroup: at most one drawn from the group (exclusion) ---

    [Fact]
    public void ConflictGroup_WithTwoDistinctMembers_ReportsAnUpperBound()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));
        selection.Add(engine, Cabin, new InstanceData("e"));        // two mutually-exclusive members

        var rule = new ConflictGroup<Product, Dependency, InstanceData>(wheel, engine);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Fact]
    public void ConflictGroup_TwoOccurrencesOfOneMemberAreNotAConflict()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(wheel, RearAxle, new InstanceData("b"));      // two of the SAME alternative

        // The group excludes mixing distinct alternatives; repeating one is InstanceLimit's concern.
        var rule = new ConflictGroup<Product, Dependency, InstanceData>(wheel, engine);
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void ConflictGroup_WithAtMostOnePresent_IsSatisfied()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));

        var rule = new ConflictGroup<Product, Dependency, InstanceData>(wheel, engine);
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void ConflictGroup_RejectsAnEmptyGroup()
    {
        Assert.Throws<ArgumentException>(() => new ConflictGroup<Product, Dependency, InstanceData>());
    }

    // --- RequiresEdge: a requires-typed base-graph edge is an AND-dependency (edges feed rules) ---

    private static bool IsRequires(Dependency d) => d.Kind == "requires";

    [Fact]
    public void RequiresEdge_SourcePresentTargetAbsent_ReportsALowerBound()
    {
        var (graph, wheel, engine, _) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("requires"));   // engine requires wheel
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));    // engine present, wheel absent

        var rule = new RequiresEdge<Product, Dependency, InstanceData>(IsRequires);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    [Fact]
    public void RequiresEdge_SatisfiedWhenTheTargetIsPresent()
    {
        var (graph, wheel, engine, _) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("requires"));
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));
        selection.Add(wheel, FrontAxle, new InstanceData("w"));     // target now present

        var rule = new RequiresEdge<Product, Dependency, InstanceData>(IsRequires);
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void RequiresEdge_DoesNotFireWhenTheSourceIsAbsent()
    {
        var (graph, wheel, engine, _) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("requires"));
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("w"));     // only the target; source unselected

        var rule = new RequiresEdge<Product, Dependency, InstanceData>(IsRequires);
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void RequiresEdge_IgnoresEdgesThePredicateRejects()
    {
        var (graph, wheel, engine, _) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("conflicts"));  // not a requires edge
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));

        var rule = new RequiresEdge<Product, Dependency, InstanceData>(IsRequires);
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void RequiresEdge_AndsEveryOutgoingDependency()
    {
        var (graph, wheel, engine, seat) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("requires"));   // engine requires wheel
        graph.AddEdge(engine, seat, new Dependency("requires"));    // and seat
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));    // both targets absent

        var rule = new RequiresEdge<Product, Dependency, InstanceData>(IsRequires);
        var violations = Check(graph, selection, rule);

        Assert.Equal(2, violations.Count);                          // one per unmet edge (AND)
        Assert.All(violations, v => Assert.Equal(ViolationKind.LowerBound, v.Kind));
    }

    [Fact]
    public void RequiresEdge_NeverMutatesTheBaseGraph()
    {
        var (graph, wheel, engine, _) = Fixture();
        var edge = graph.AddEdge(engine, wheel, new Dependency("requires"));
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));

        var nodesBefore = graph.Nodes.ToArray();
        var edgesBefore = graph.Edges.ToArray();

        _ = Check(graph, selection, new RequiresEdge<Product, Dependency, InstanceData>(IsRequires));

        Assert.Equal(nodesBefore, graph.Nodes);
        Assert.Equal(edgesBefore, graph.Edges);
        Assert.Equal(new Dependency("requires"), graph.GetEdgePayload(edge));
    }

    [Fact]
    public void RequiresEdge_RejectsANullPredicate()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RequiresEdge<Product, Dependency, InstanceData>(null!));
    }

    // --- Evaluator handle index: prune to the rules a candidate could affect, without changing verdicts ---

    [Fact]
    public void Index_BucketsABuiltInUnderEveryHandleItReferences()
    {
        var (_, wheel, engine, _) = Fixture();
        var wheelRule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, 1, 4);
        var group = new OneOf<Product, Dependency, InstanceData>(wheel, engine);

        var index = Evaluator.Index<Product, Dependency, InstanceData>([wheelRule, group]);

        // Both the wheel Cardinality and the wheel-or-engine group reference the wheel handle.
        Assert.Equal([wheelRule, group], index.RulesReferencing(wheel));
        // Only the group references the engine handle.
        Assert.Equal([group], index.RulesReferencing(engine));
    }

    [Fact]
    public void Index_PrunesBuiltInsThatDoNotReferenceTheCandidate()
    {
        var (_, wheel, engine, seat) = Fixture();
        var wheelRule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, 1, 4);

        var index = Evaluator.Index<Product, Dependency, InstanceData>([wheelRule]);

        // A candidate of a prototype no rule mentions maps to no rules — the pruning that keeps
        // Availability off O(candidates × allRules).
        Assert.Empty(index.RulesReferencing(seat));
    }

    [Fact]
    public void Index_TreatsGraphReadingAndCustomRulesAsAlwaysRelevant()
    {
        var (_, wheel, engine, seat) = Fixture();
        var requires = new RequiresEdge<Product, Dependency, InstanceData>(IsRequires);
        var custom = new AtMostOnePerRegion();
        var wheelRule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, 1, 4);

        var index = Evaluator.Index<Product, Dependency, InstanceData>([requires, custom, wheelRule]);

        // RequiresEdge (graph-discovered handles) and the custom rule can't be pinned to a handle, so
        // they surface for every candidate — even one no built-in mentions.
        Assert.Equal([requires, custom], index.AlwaysRelevantRules);
        Assert.Equal([requires, custom], index.RulesReferencing(seat));
        Assert.Equal([requires, custom, wheelRule], index.RulesReferencing(wheel));
    }

    // --- Region-structural constraints as custom IRule over the SelectionView (ADR 0007 tier) ---

    // "At most one instance per region" — the same substrate the earlier suite used, reused here as the
    // baseline custom region rule the index must treat as always-relevant.
    private sealed class AtMostOnePerRegion : IRule<Product, Dependency, InstanceData>
    {
        public IEnumerable<Violation> Check(SelectionView<Product, Dependency, InstanceData> view)
        {
            foreach (var region in view.ActiveRegions)
            {
                if (view.RegionCount(region) > 1)
                {
                    yield return new Violation(ViolationKind.UpperBound, $"Region {region} holds more than one instance.");
                }
            }
        }
    }

    // "At most N active regions" — a constraint over regions-as-entities, left to custom IRule in v1
    // (ADR 0007). Expressible with no library change against ActiveRegions.
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

    // "Closed set of regions" — every active region must be one of the allowed set (ADR 0007).
    private sealed class ClosedRegionSet(params Region[] allowed) : IRule<Product, Dependency, InstanceData>
    {
        public IEnumerable<Violation> Check(SelectionView<Product, Dependency, InstanceData> view)
        {
            foreach (var region in view.ActiveRegions)
            {
                if (!allowed.Contains(region))
                {
                    yield return new Violation(
                        ViolationKind.UpperBound, $"Region {region} is outside the closed set.");
                }
            }
        }
    }

    // "If region A is active then region B must be active" — a cross-region implication (ADR 0007).
    private sealed class IfActiveThenActive(Region antecedent, Region consequent)
        : IRule<Product, Dependency, InstanceData>
    {
        public IEnumerable<Violation> Check(SelectionView<Product, Dependency, InstanceData> view)
        {
            if (view.ActiveRegions.Contains(antecedent) && !view.ActiveRegions.Contains(consequent))
            {
                yield return new Violation(
                    ViolationKind.LowerBound, $"Region {antecedent} active but required region {consequent} is not.");
            }
        }
    }

    [Fact]
    public void CustomRule_AtMostNActiveRegions_IsExpressibleAndParticipates()
    {
        var (graph, wheel, engine, seat) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(engine, RearAxle, new InstanceData("b"));
        selection.Add(seat, Cabin, new InstanceData("c"));          // 3 active regions

        var rule = new AtMostActiveRegions(2);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Fact]
    public void CustomRule_ClosedRegionSet_IsExpressibleAndParticipates()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));
        selection.Add(engine, Cabin, new InstanceData("b"));        // Cabin not in the allowed set

        var rule = new ClosedRegionSet(FrontAxle, RearAxle);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Fact]
    public void CustomRule_IfAThenB_IsExpressibleAndParticipates()
    {
        var (graph, wheel, engine, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("a"));     // FrontAxle active, RearAxle not

        var rule = new IfActiveThenActive(FrontAxle, RearAxle);
        var violation = Assert.Single(Check(graph, selection, rule));
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);

        selection.Add(engine, RearAxle, new InstanceData("b"));     // now the consequent holds
        Assert.Empty(Check(graph, selection, rule));
    }

    [Fact]
    public void BuiltInsAndCustomRegionRules_UnionIdenticallyThroughOneCheck()
    {
        var (graph, wheel, engine, seat) = Fixture();
        graph.AddEdge(engine, wheel, new Dependency("requires"));
        var selection = new Selection<InstanceData>();
        selection.Add(engine, Cabin, new InstanceData("motor"));    // engine requires wheel (absent)
        selection.Add(seat, Cabin, new InstanceData("s1"));
        selection.Add(seat, Cabin, new InstanceData("s2"));         // Cabin holds >1 -> custom breach

        var violations = Evaluator.Check(graph, selection, new IRule<Product, Dependency, InstanceData>[]
        {
            new RequiresEdge<Product, Dependency, InstanceData>(IsRequires),
            new AtMostOnePerRegion(),
        });

        Assert.Contains(violations, v => v.Kind == ViolationKind.LowerBound);   // requires edge unmet
        Assert.Contains(violations, v => v.Kind == ViolationKind.UpperBound);   // custom per-region cap
    }
}
