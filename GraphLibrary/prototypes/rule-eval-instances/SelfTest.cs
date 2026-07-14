// PROTOTYPE — throwaway headless check (no ReadKey). Confirms both models stay
// in lockstep and the rule engine reacts as expected. `dotnet run -- --selftest`.
namespace RuleEvalPrototype;

public static class SelfTest
{
    public static void Run()
    {
        var i = new CoreNativeModel();
        var o = new OverlayModel();
        int id = 1;
        void Add(string p, string l) { i.AddInstance(new(id), p, l); o.AddInstance(new(id), p, l); id++; }
        void Assert(bool c, string m) => Console.WriteLine((c ? "PASS " : "FAIL ") + m);

        // Build: TurboKit in Chassis (violates AND: needs Engine), 3 wheels in FrontAxle
        Add("TurboKit", "Chassis");
        Add("Wheel", "FrontAxle");
        Add("Wheel", "FrontAxle");
        Add("Wheel", "FrontAxle");   // 3 in one axle -> LayerCardinality max 2

        var vi = i.Project(); var vo = o.Project();
        Assert(vi.All.Count == vo.All.Count && vi.All.Count == 4, "both models project 4 instances");

        var ri = Engine_.Validate(vi); var ro = Engine_.Validate(vo);
        Assert(ri.Violations.Count == ro.Violations.Count, "both models -> identical violation count");
        Assert(ri.Violations.Any(x => x.Message.Contains("requires Engine")), "AND dep fires (TurboKit needs Engine)");
        Assert(ri.Violations.Any(x => x.Kind == ViolationKind.UpperBound && x.Message.Contains("FrontAxle")),
            "LayerCardinality fires (3 wheels in an axle > 2)");

        // Availability: a 4th Wheel into FrontAxle should be BLOCKED (cardinality),
        // but into RearAxle should be AVAILABLE.
        var af = Engine_.AvailabilityIn(vi, "FrontAxle", id);
        var ar = Engine_.AvailabilityIn(vi, "RearAxle", id);
        Assert(af.Single(a => a.Prototype == "Wheel").CanAdd == false, "Wheel blocked in full FrontAxle");
        Assert(ar.Single(a => a.Prototype == "Wheel").CanAdd == true, "Wheel available in empty RearAxle");

        // Conflict: add StereoA then StereoB -> exclusion; StereoB availability blocked once A present
        Add("StereoA", "Chassis");
        var withA = i.Project();
        var availB = Engine_.AvailabilityIn(withA, "Chassis", id);
        Assert(availB.Single(a => a.Prototype == "StereoB").CanAdd == false, "StereoB blocked while StereoA present");

        // Structure divergence check: representations differ, projections match
        Assert(i.DumpStructure().Contains("instance-of"), "model (i) stores instances as nodes+edges");
        Assert(o.DumpStructure().Contains("overlay"), "model (ii) stores instances in overlay, base unchanged");
    }
}
