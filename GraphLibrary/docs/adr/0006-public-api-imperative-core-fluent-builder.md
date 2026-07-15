# 0006: Imperative handle-based core with an optional keyed fluent builder

## Status

Accepted

## Context

Construction spans two profiles: big/dynamic graphs (often keyless) and small/well-defined graphs (natural keys, forward references). One idiom must serve both without fragmenting the type surface, and query must stay discoverably distinct from rule-evaluation.

## Decision

**One imperative handle-based core plus an optional keyed fluent builder over it.**

- **Imperative core (universal path).** Handle-returning mutating members on the concrete `Graph<TNode,TEdge>`: `AddNode`, `AddEdge`, `SetPayload` (the observable-mutation channel [ADR 0003](./0003-opaque-handle-identity.md) requires), `RemoveNode` / `RemoveEdge`. No separate writer type — the concrete `Graph` **implements `IReadableGraph`**, so the same object flows straight into traversal and rule-eval.
- **Fluent builder (small/well-defined graphs), gated behind a unique keyed `SecondaryIndex`.** `Graph.Build<TNode,TEdge,TKey>(keySelector)` derives keys from payloads, references edges **by key** with deferred resolution (forward refs OK), and `.Build()` returns the *same* mutable `Graph` with the index still enforced. This reuses one key concept ([ADR 0002](./0002-incidence-list-storage-slotmap-backing.md)'s `SecondaryIndex`) rather than throwaway build-scoped keys.

**Coherence via a hub + intentional asymmetry.** The concrete `Graph` is the single hub the caller holds; it is kept as permissive as possible, and rule-eval stays distinct:

- **Query = extension methods on the graph** (`GraphLibrary.Traversal`, [ADR 0004](./0004-layered-traversal-api-extensions.md)).
- **Rule-eval = a separate engine that *takes* the graph** (`GraphLibrary.Rules`, [ADR 0005](./0005-rule-evaluation-checker-oracle-overlay.md)) — not a method on `Graph`.
- Discoverability by **namespace**: a caller who never imports `.Rules` never sees rule types; the surface scales down to "just a graph."

**Rule surface = plain object model, no DSL.** Custom rules implement `IRule`; built-ins are plain-constructor classes (`RequiresEdge`, `OneOf`, `ConflictGroup`, `Cardinality`, `InstanceLimit`); the rule-set is a flat collection; the Selection overlay is imperative (`Add → instanceId`, `Remove`). To keep Availability off O(candidates × allRules), the Evaluator **indexes rules by referenced handle automatically** for built-ins plus internal selection tracking (zero author burden); a **custom** rule may **optionally declare a `Scope`** to opt into indexing (undeclared → global, correct but unindexed) — a performance opt-in that never affects correctness.

**Errors:** throw for programmer bugs (invalid/stale handle, key collision); `Try*` for expected misses; `bool` for removal; `InvalidOperationException` on mid-iteration structural mutation (per [ADR 0004](./0004-layered-traversal-api-extensions.md)).

## Consequences

- The surface scales from "just a graph" up to full rule-eval by namespace import, not type proliferation.
- The fluent builder is a typed, key-required convenience; keyless payloads simply use the imperative path.
- The optional `Scope` refines [ADR 0005](./0005-rule-evaluation-checker-oracle-overlay.md)'s otherwise-bare `IRule` with one optional member — a refinement, not a contradiction.

Originating decision: wayfinder map [#1](https://github.com/larced/pipe/issues/1), ticket [#7](https://github.com/larced/pipe/issues/7).
