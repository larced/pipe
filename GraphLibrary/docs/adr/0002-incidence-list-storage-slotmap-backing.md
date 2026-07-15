# 0002: Incidence-list storage with a slot-map backing; adjacency matrix rejected

## Status

Accepted

## Context

Target scale is ~25,000–50,000 nodes, sparse (~avg out-degree 8), with headroom; priorities are performance > memory > ease-of-use. Two questions: the logical storage model, and the physical container behind it.

## Decision

**Incidence-list model:** a node store (Handle → Payload + Incidence), a central edge store (Handle → `{source, target, TEdge}`, single-sourcing edge identity), and per-node **in- and out-incidence** indices into the edge store. An **adjacency matrix is ruled out** — infeasible at 50k² and unable to represent a multigraph or first-class edge identity.

Both incidence directions are indexed (serves reverse queries, cycle reasoning, and clean removal; cost is small and linear in |E|). Endpoint-pair access is **derived** by scanning the source's out-incidence (O(out-degree), cheap when sparse) — **no dedicated `(source, target)` index**, additive later if proven hot. The core is handle-only with **no built-in payload-content indexing**; content lookup is an opt-in `SecondaryIndex<TKey>` fed by an opt-in change-notification surface.

Physical backing is a **generational-index slot-map** (generational arrays for the node, edge, and incidence stores; free-lists reclaim slots; generation counters ride in the Handle), chosen by benchmark over dictionary-keyed and hybrid options. It wins traversal and memory at every scale, and its lead over the dictionary *widens* with size (~7× faster at 1k → ~48× at 50k, and ~2.5× less memory) as the structure outgrows CPU cache — so at the target scale the lead grows, not shrinks.

## Consequences

- The public API is independent of the container: identity is an opaque Handle ([ADR 0003](./0003-opaque-handle-identity.md)), so the slot-map choice — and any future denser incidence representation (CSR-style, or an inline small-buffer for low-degree nodes, flagged as a perf lever) — never changes the surface.
- Reverse queries ("what depends on X") and node removal are cheap; the second incidence direction costs a small linear-in-|E| memory overhead.
- No content indexing in core keeps it domain-agnostic; the domain layer (Catalog) builds indices via `SecondaryIndex`.

Originating decisions: wayfinder map [#1](https://github.com/larced/pipe/issues/1), tickets [#5](https://github.com/larced/pipe/issues/5) (model) and [#8](https://github.com/larced/pipe/issues/8) (backing-container benchmark).
