# 0003: Opaque handle identity, independent of payload

## Status

Accepted

## Context

Storage ([ADR 0002](./0002-incidence-list-storage-slotmap-backing.md)) requires an identity independent of the `TNode`/`TEdge` Payload (payloads may duplicate and mutate). [ADR 0001](./0001-single-multigraph-core-layered-validators.md) already made Edges first-class with their own identity; the symmetric question for Nodes was left open.

## Decision

A Node and an Edge are each identified by a **stable, opaque Handle** (`NodeHandle` / `EdgeHandle`) — plain, non-generic `readonly struct`s, allocation-free, usable as dictionary keys — independent of Payload and unaffected by payload mutation. Payload is mutable in place through an **observable channel** (`SetPayload`), so content indices stay correct; duplicate payloads are allowed. Payload→handle lookup is the opt-in `SecondaryIndex<TKey>` ([ADR 0002](./0002-incidence-list-storage-slotmap-backing.md)), not core.

Handles carry an internal id plus a generation/graph stamp, giving a **runtime cross-graph / stale-handle guard**. They are deliberately **not** type-parameterized by `TNode`/`TEdge`: this keeps every signature lean, and the runtime stamp is needed regardless to catch same-typed-graph mixing (the likelier error), which a generic handle could not prevent anyway.

## Consequences

- Payload mutation never disturbs graph structure or invalidates a Handle.
- Every signature across traversal and rule-evaluation stays lean — no `TNode`/`TEdge` riding on the handle.
- A misused or stale Handle fails fast at runtime (`InvalidHandleException`) rather than silently reading the wrong element.

Originating decision: wayfinder map [#1](https://github.com/larced/pipe/issues/1), ticket [#4](https://github.com/larced/pipe/issues/4) (node identity), symmetric with the edge identity settled in ticket [#2](https://github.com/larced/pipe/issues/2).
