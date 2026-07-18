namespace GraphLibrary.Rules;

/// <summary>
/// An index of rules by the prototype <see cref="NodeHandle"/> they reference (CONTEXT.md → Evaluator,
/// ADR 0005), built by <see cref="Evaluator.Index{TNode,TEdge,TInstanceData}"/>. It answers "which
/// rules could a candidate of prototype P affect?" — the substrate a later Availability derivation uses
/// to stay off <c>O(candidates × allRules)</c>: rather than re-Checking every rule for every candidate,
/// it re-Checks only <see cref="RulesReferencing"/> that candidate's prototype.
/// </summary>
/// <remarks>
/// <para>
/// A rule that declares its handles via <see cref="IHandleReferencing"/> (the built-ins
/// <see cref="Cardinality{TNode,TEdge,TInstanceData}"/>, <see cref="OneOf{TNode,TEdge,TInstanceData}"/>,
/// <see cref="ConflictGroup{TNode,TEdge,TInstanceData}"/>) is bucketed under each handle it names. Any
/// other rule — a custom <see cref="IRule{TNode,TEdge,TInstanceData}"/>, or a built-in like
/// <see cref="RequiresEdge{TNode,TEdge,TInstanceData}"/> whose relevant handles are discovered from the
/// graph — is treated as <b>always relevant</b> and returned for <em>every</em> handle. So the index
/// only prunes rules it can prove irrelevant and never changes which violations a full
/// <see cref="Evaluator.Check{TNode,TEdge,TInstanceData}"/> would find — it is an evaluation
/// optimisation, not a semantic one.
/// </para>
/// <para>
/// The index is an immutable snapshot of the rule set handed to it; it holds the rule references but
/// never evaluates them.
/// </para>
/// </remarks>
public sealed class RuleIndex<TNode, TEdge, TInstanceData>
{
    private readonly Dictionary<NodeHandle, List<IRule<TNode, TEdge, TInstanceData>>> _byHandle = new();

    // Rules that did not declare specific handles (custom rules, RequiresEdge): relevant to every
    // handle, so they join every RulesReferencing answer rather than being pruned.
    private readonly List<IRule<TNode, TEdge, TInstanceData>> _alwaysRelevant = [];

    internal RuleIndex(IEnumerable<IRule<TNode, TEdge, TInstanceData>> rules)
    {
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule, nameof(rules));

            var handles = (rule as IHandleReferencing)?.ReferencedHandles;
            if (handles is null)
            {
                _alwaysRelevant.Add(rule);
                continue;
            }

            // Distinct so a rule that names the same handle twice (e.g. a group with a repeated
            // prototype) is bucketed under it only once.
            var declaredAny = false;
            foreach (var handle in handles.Distinct())
            {
                declaredAny = true;
                if (!_byHandle.TryGetValue(handle, out var bucket))
                {
                    bucket = [];
                    _byHandle[handle] = bucket;
                }

                bucket.Add(rule);
            }

            // A rule that declares the capability but names no handle constrains nothing in particular;
            // treat it as always relevant so it is never silently dropped.
            if (!declaredAny)
            {
                _alwaysRelevant.Add(rule);
            }
        }
    }

    /// <summary>
    /// The rules whose verdict a candidate of <paramref name="prototype"/> could change: every rule
    /// that declared <paramref name="prototype"/> among its referenced handles, plus every
    /// always-relevant rule. Order is not promised; each distinct rule appears once.
    /// </summary>
    public IReadOnlyList<IRule<TNode, TEdge, TInstanceData>> RulesReferencing(NodeHandle prototype)
    {
        if (_alwaysRelevant.Count == 0)
        {
            // Copy the bucket so the returned snapshot cannot be cast back and mutated.
            return _byHandle.TryGetValue(prototype, out var only) ? [.. only] : [];
        }

        var result = new List<IRule<TNode, TEdge, TInstanceData>>(_alwaysRelevant);
        if (_byHandle.TryGetValue(prototype, out var bucket))
        {
            result.AddRange(bucket);
        }

        return result;
    }

    /// <summary>
    /// The rules that reference no specific handle and so are evaluated for every candidate — custom
    /// rules and graph-reading built-ins like <see cref="RequiresEdge{TNode,TEdge,TInstanceData}"/>.
    /// Exposed so a caller (and this suite) can see what the index could not prune. A fresh snapshot,
    /// so it cannot be cast back and mutated.
    /// </summary>
    public IReadOnlyList<IRule<TNode, TEdge, TInstanceData>> AlwaysRelevantRules => [.. _alwaysRelevant];
}
