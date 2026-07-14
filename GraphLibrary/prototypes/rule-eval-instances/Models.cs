// PROTOTYPE — throwaway. The structural fork: two ways to store instances+layers.
namespace RuleEvalPrototype;

public interface IConfigModel
{
    string Name { get; }
    void AddInstance(InstanceId id, string prototype, string layer);
    void RemoveLast();
    SelectionView Project();       // the shared view rules read
    string DumpStructure();        // how THIS model physically stores it
}

/// Model (i): instances are REAL graph nodes; layer-membership and instance-of are EDGES.
/// The base graph grows as you select. Prototypes are "type" nodes already in the graph.
public sealed class CoreNativeModel : IConfigModel
{
    public string Name => "(i) Core-native graph";

    private sealed record Node(string Id, string Kind, string? Prototype, string? Layer);
    private sealed record Edge(string From, string Rel, string To);

    private readonly List<Node> _nodes = new();
    private readonly List<Edge> _edges = new();
    private readonly List<string> _order = new();

    public CoreNativeModel()
    {
        foreach (var p in Scenario.Prototypes) _nodes.Add(new(p, "prototype", null, null));
        foreach (var l in Scenario.Layers) _nodes.Add(new(l, "layer", null, null));
    }

    public void AddInstance(InstanceId id, string prototype, string layer)
    {
        var nid = $"inst#{id.Value}";
        _nodes.Add(new(nid, "instance", prototype, layer));
        _edges.Add(new(nid, "instance-of", prototype));
        _edges.Add(new(nid, "member-of", layer));
        _order.Add(nid);
    }

    public void RemoveLast()
    {
        if (_order.Count == 0) return;
        var nid = _order[^1]; _order.RemoveAt(_order.Count - 1);
        _nodes.RemoveAll(n => n.Id == nid);
        _edges.RemoveAll(e => e.From == nid);
    }

    // View is DERIVED by walking instance nodes + their edges.
    public SelectionView Project() => new(
        _nodes.Where(n => n.Kind == "instance")
              .Select(n => new Instance(
                  new InstanceId(int.Parse(n.Id.Split('#')[1])), n.Prototype!, n.Layer!))
              .ToList());

    public string DumpStructure()
    {
        var instNodes = _nodes.Count(n => n.Kind == "instance");
        var s = $"  graph nodes: {_nodes.Count} ({Scenario.Prototypes.Length} prototype + " +
                $"{Scenario.Layers.Length} layer + {instNodes} instance),  edges: {_edges.Count}\n";
        foreach (var n in _nodes.Where(n => n.Kind == "instance"))
            s += $"    +node {n.Id}  --instance-of-->{n.Prototype}  --member-of-->{n.Layer}\n";
        if (instNodes == 0) s += "    (no instance nodes yet — graph == base)\n";
        return s;
    }
}

/// Model (ii): base graph stays LEAN (handles only). Instances + layer tags live in a
/// separate selection overlay above the graph. The graph never changes.
public sealed class OverlayModel : IConfigModel
{
    public string Name => "(ii) Selection overlay";
    private readonly List<Instance> _overlay = new();

    public void AddInstance(InstanceId id, string prototype, string layer)
        => _overlay.Add(new Instance(id, prototype, layer));

    public void RemoveLast()
    {
        if (_overlay.Count > 0) _overlay.RemoveAt(_overlay.Count - 1);
    }

    public SelectionView Project() => new(_overlay.ToList());

    public string DumpStructure()
    {
        var s = $"  base graph: {Scenario.Prototypes.Length} handles (UNCHANGED) " +
                $"[{string.Join(", ", Scenario.Prototypes)}]\n" +
                $"  overlay selection: {_overlay.Count} instance(s)\n";
        foreach (var i in _overlay)
            s += $"    {{ id={i.Id.Value}, proto={i.Prototype}, layer={i.Layer} }}\n";
        if (_overlay.Count == 0) s += "    (empty overlay)\n";
        return s;
    }
}
