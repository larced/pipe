using GraphLibrary;
using GraphLibrary.Rules;

namespace GraphLibrary.Tests;

// Seam 3 — Rule evaluation, ticket 14 (#25): the rule-evaluation tracer. Delivers the region-aware
// SelectionView every rule reads, the IRule extension point, classified Violations, and
// Evaluator.Check unioning violations order-free — proven end-to-end with one built-in rule
// (Cardinality) and a custom rule participating identically. Covers spec stories 50-53, 57, 70.
// Every assertion drives only the public Rules API over a real base Graph, so the "Check over a
// selection overlay that never mutates the base graph" contract is enforced by the suite (ADR 0005).
public class RuleEvaluationTests
{
    private sealed record Product(string Name);
    private sealed record Dependency(string Kind);
    private sealed record InstanceData(string Note);

    private static readonly Region FrontAxle = new("FrontAxle");
    private static readonly Region RearAxle = new("RearAxle");

    private static (Graph<Product, Dependency> graph, NodeHandle wheel, NodeHandle engine) Fixture()
    {
        var graph = new Graph<Product, Dependency>();
        var wheel = graph.AddNode(new Product("wheel"));
        var engine = graph.AddNode(new Product("engine"));
        return (graph, wheel, engine);
    }

    // --- SelectionView: active regions, an instance's region, and the counting primitive ---

    [Fact]
    public void View_ExposesActiveRegions_AndAnInstancesRegion()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        var front = selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, RearAxle, new InstanceData("spare"));

        var view = new SelectionView<Product, Dependency, InstanceData>(graph, selection);

        Assert.Equal([FrontAxle, RearAxle], view.ActiveRegions.OrderBy(r => r.Label));
        Assert.Equal(FrontAxle, view.RegionOf(front));
    }

    [Fact]
    public void View_RegionOf_ThrowsForAnIdNotInTheView()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        var id = selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Remove(id);

        var view = new SelectionView<Product, Dependency, InstanceData>(graph, selection);

        Assert.Throws<ArgumentException>(() => view.RegionOf(id));
    }

    [Fact]
    public void View_CountsPerRegion_CrossRegion_AndGlobal()
    {
        var (graph, wheel, engine) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, FrontAxle, new InstanceData("right"));
        selection.Add(wheel, RearAxle, new InstanceData("spare"));
        selection.Add(engine, FrontAxle, new InstanceData("only"));

        var view = new SelectionView<Product, Dependency, InstanceData>(graph, selection);

        Assert.Equal(4, view.GlobalCount);                                  // global
        Assert.Equal(3, view.RegionCount(FrontAxle));                       // per-region
        Assert.Equal(4, view.RegionSetCount([FrontAxle, RearAxle]));        // cross-region (named set)
        // The one primitive, narrowed by an `among` predicate: wheels in the front region only.
        Assert.Equal(2, view.Count(r => r == FrontAxle, i => i.Prototype == wheel));
    }

    [Fact]
    public void View_IsAnEagerSnapshot_UnaffectedByLaterSelectionMutation()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));

        var view = new SelectionView<Product, Dependency, InstanceData>(graph, selection);
        selection.Add(wheel, RearAxle, new InstanceData("right"));

        Assert.Equal(1, view.GlobalCount);
        Assert.Single(view.Instances);
    }

    // --- Cardinality (global scope): lower-bound and upper-bound classification ---

    [Fact]
    public void Cardinality_BelowMin_ReportsALowerBoundViolation()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, min: 2, max: 4);
        var violations = Evaluator.Check(graph, selection, [rule]);

        var violation = Assert.Single(violations);
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    [Fact]
    public void Cardinality_AboveMax_ReportsAnUpperBoundViolation()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, FrontAxle, new InstanceData("right"));
        selection.Add(wheel, RearAxle, new InstanceData("spare"));

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, min: 0, max: 2);
        var violations = Evaluator.Check(graph, selection, [rule]);

        var violation = Assert.Single(violations);
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Fact]
    public void Cardinality_WithinBand_ReportsNoViolation()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, RearAxle, new InstanceData("right"));

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, min: 1, max: 4);
        var violations = Evaluator.Check(graph, selection, [rule]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Cardinality_CountsOnlyTheTargetPrototype()
    {
        var (graph, wheel, engine) = Fixture();
        var selection = new Selection<InstanceData>();
        // Three engines but only one wheel: a [2,4]-wheel rule must still see a shortfall.
        selection.Add(engine, FrontAxle, new InstanceData("a"));
        selection.Add(engine, FrontAxle, new InstanceData("b"));
        selection.Add(engine, RearAxle, new InstanceData("c"));
        selection.Add(wheel, FrontAxle, new InstanceData("left"));

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, min: 2, max: 4);
        var violations = Evaluator.Check(graph, selection, [rule]);

        var violation = Assert.Single(violations);
        Assert.Equal(ViolationKind.LowerBound, violation.Kind);
    }

    [Fact]
    public void InstanceLimit_IsGlobalCardinalityZeroToOne()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, RearAxle, new InstanceData("right"));

        var rule = Cardinality<Product, Dependency, InstanceData>.InstanceLimit(wheel);
        var violations = Evaluator.Check(graph, selection, [rule]);

        var violation = Assert.Single(violations);
        Assert.Equal(ViolationKind.UpperBound, violation.Kind);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 2)]
    public void Cardinality_RejectsAnUnsatisfiableBand(int min, int max)
    {
        var (_, wheel, _) = Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, min, max));
    }

    // --- Evaluator.Check: flat, conjunctive, order-free union with no precedence / short-circuit ---

    [Fact]
    public void Check_OfAnEmptyRuleSet_IsValid()
    {
        var (graph, wheel, _) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));

        var violations = Evaluator.Check(
            graph, selection, Array.Empty<IRule<Product, Dependency, InstanceData>>());

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_UnionsEveryRulesViolations_WithNoShortCircuit()
    {
        var (graph, wheel, engine) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left")); // 1 wheel, 0 engines

        // Two rules both breach: too few wheels (lower) and too few engines (lower). The union must
        // carry both — no rule suppresses or precedes another.
        var wheels = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, 2, 4);
        var engines = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, engine, 1, 1);

        var violations = Evaluator.Check(graph, selection, [wheels, engines]);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, v => Assert.Equal(ViolationKind.LowerBound, v.Kind));
    }

    [Fact]
    public void Check_IsOrderFree_SameViolationSetRegardlessOfRuleOrder()
    {
        var (graph, wheel, engine) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));
        selection.Add(wheel, FrontAxle, new InstanceData("right"));
        selection.Add(wheel, RearAxle, new InstanceData("spare"));

        var tooManyWheels = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, 0, 2);
        var tooFewEngines = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, engine, 1, 1);

        var forward = Evaluator.Check(graph, selection, [tooManyWheels, tooFewEngines]);
        var reversed = Evaluator.Check(graph, selection, [tooFewEngines, tooManyWheels]);

        Assert.Equal(
            forward.OrderBy(v => v.Message),
            reversed.OrderBy(v => v.Message));
    }

    // --- A custom rule participates identically to a built-in (ADR 0005) ---

    // "At most one instance per region" — expressible only against the region-aware view, and with no
    // library change. It participates through the same IRule seam as the built-in Cardinality.
    private sealed class AtMostOnePerRegion : IRule<Product, Dependency, InstanceData>
    {
        public IEnumerable<Violation> Check(SelectionView<Product, Dependency, InstanceData> view)
        {
            foreach (var region in view.ActiveRegions)
            {
                var count = view.RegionCount(region);
                if (count > 1)
                {
                    yield return new Violation(
                        ViolationKind.UpperBound, $"Region {region} holds {count} instances; at most 1 allowed.");
                }
            }
        }
    }

    [Fact]
    public void CustomRule_ParticipatesIdenticallyToBuiltIns()
    {
        var (graph, wheel, engine) = Fixture();
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));  // FrontAxle now has 2 -> custom breach
        selection.Add(engine, FrontAxle, new InstanceData("only"));
        selection.Add(wheel, RearAxle, new InstanceData("spare"));  // RearAxle has 1 -> ok

        // A built-in and a custom rule, side by side, unioned with no special-casing.
        var builtIn = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, 3, 4);
        var custom = new AtMostOnePerRegion();

        var violations = Evaluator.Check(graph, selection, [builtIn, custom]);

        Assert.Contains(violations, v => v.Kind == ViolationKind.LowerBound);   // built-in: too few wheels
        Assert.Contains(violations, v => v.Kind == ViolationKind.UpperBound);   // custom: FrontAxle > 1
    }

    // --- The load-bearing contract: Check never mutates the base graph (ADR 0005) ---

    [Fact]
    public void Check_NeverMutatesTheBaseGraph()
    {
        var (graph, wheel, engine) = Fixture();
        var edge = graph.AddEdge(engine, wheel, new Dependency("requires"));
        var selection = new Selection<InstanceData>();
        selection.Add(wheel, FrontAxle, new InstanceData("left"));

        var nodesBefore = graph.Nodes.ToArray();
        var edgesBefore = graph.Edges.ToArray();

        var rule = new Cardinality<Product, Dependency, InstanceData>(Scope.Global, wheel, 2, 4);
        _ = Evaluator.Check(graph, selection, [rule]);

        Assert.Equal(nodesBefore, graph.Nodes);
        Assert.Equal(edgesBefore, graph.Edges);
        Assert.Equal(new Product("wheel"), graph.GetNodePayload(wheel));
        Assert.Equal(new Dependency("requires"), graph.GetEdgePayload(edge));
    }
}
