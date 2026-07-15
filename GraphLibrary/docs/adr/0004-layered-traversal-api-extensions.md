# 0004: Layered traversal API as extension methods over a read interface, distinct from rule-eval

## Status

Accepted

## Context

The query surface must be discoverable, fast on hot primitives, and physically separate from rule evaluation (a distinct concern). It must also be the shared seam the rule layer consumes, not a parallel engine.

## Decision

A **layered** API. The lean core exposes `IReadableGraph<TNode,TEdge>` — nodes, edges, in/out Incidence, degree as **real members** (static-dispatch on the hot path). Reachability, paths, ordering, components, and advisory cycle queries ship as **extension methods in a separate `Traversal` namespace** over that interface — making the query≠rule-evaluation boundary physical and letting the rule layer ([ADR 0005](./0005-rule-evaluation-checker-oracle-overlay.md)) consume the same read surface without an adapter.

The primitive engine is **pull-primary** (lazy, LINQ-composable `Traverse`) with a **visitor** (`Continue` / `SkipDescendants` / `Stop`) underneath for the named algorithms; a **visited-set** guarantees termination despite cycles, self-loops, and parallel edges. **Paths are edge-handle sequences** (unambiguous under parallel edges); **reachability returns a node set**. Filtering is inline `Func<TNode,bool>` / `Func<TEdge,bool>` predicates plus opt-in `SecondaryIndex` for accelerated seed selection — **no query DSL**. `ShortestPath` is BFS by default, opt-in Dijkstra for non-negative weights; Bellman-Ford / negative weights ruled out (YAGNI).

Liveness (single-threaded): materialized results (sets, paths, components) are **eager snapshots**; the lazy `Traverse` is **fail-fast** on mid-iteration structural mutation (`InvalidOperationException` via a modification counter). Real concurrency is deferred.

## Consequences

- A caller who never imports `Traversal` sees "just a graph"; the surface scales down.
- The rule layer builds on `IReadableGraph` + `Traverse`, never reaching into the store.
- K-shortest-paths (Yen's) and weighted negative-cycle algorithms are additive later without breaking the surface.

Originating decision: wayfinder map [#1](https://github.com/larced/pipe/issues/1), ticket [#4](https://github.com/larced/pipe/issues/4) (packaging refined by [#7](https://github.com/larced/pipe/issues/7)).
