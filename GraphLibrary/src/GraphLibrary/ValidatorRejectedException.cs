namespace GraphLibrary;

/// <summary>
/// Thrown when an opt-in topology <see cref="TopologyValidator"/> refuses a mutation that would
/// violate the shape it enforces — a self-loop under <see cref="TopologyValidator.NoSelfLoops"/>,
/// a parallel edge under <see cref="TopologyValidator.SimpleGraph"/>, or a cycle-closing edge under
/// <see cref="TopologyValidator.Acyclic"/> (CONTEXT.md → Validator, ADR 0001). Also raised when
/// <see cref="Graph{TNode,TEdge}.AddValidator"/> is asked to enforce a validator the graph already
/// violates, so a graph never claims a topology it does not actually hold.
/// </summary>
/// <remarks>
/// A dedicated type, distinct from <see cref="InvalidHandleException"/> (handle misuse is a
/// programmer bug; a rejected topology is the validator doing its job) and named to avoid the
/// reserved rule-evaluation term "Violation" (CONTEXT.md → Violation). Carries the
/// <see cref="Validator"/> that did the rejecting so a caller can react per topology.
/// </remarks>
public sealed class ValidatorRejectedException : Exception
{
    /// <summary>The validator whose topology the rejected mutation would have violated.</summary>
    public TopologyValidator Validator { get; }

    private ValidatorRejectedException(TopologyValidator validator, string message) : base(message)
    {
        Validator = validator;
    }

    internal static ValidatorRejectedException SelfLoop() =>
        new(TopologyValidator.NoSelfLoops,
            "The no-self-loops validator rejected this edge: its source and target are the same node.");

    internal static ValidatorRejectedException ParallelEdge() =>
        new(TopologyValidator.SimpleGraph,
            "The simple-graph validator rejected this edge: an edge already joins this ordered "
            + "source→target pair.");

    internal static ValidatorRejectedException Cycle() =>
        new(TopologyValidator.Acyclic,
            "The acyclicity validator rejected this edge: adding it would introduce a cycle.");

    internal static ValidatorRejectedException AlreadyViolated(TopologyValidator validator) =>
        new(validator,
            $"The graph cannot enforce the {validator} validator because it already violates that "
            + "topology; enable the validator before adding the topology it forbids.");
}
