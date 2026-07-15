# 0001: Single directed multigraph core; topology variants as layered validators

## Status

Accepted

## Context

The library must serve graphs of different shapes — general directed graphs, DAGs, simple graphs. The obvious route is distinct core types (`DiGraph`, `Dag`, …). But the driving use case is mostly a DAG with a **known, legitimate cyclic self-reference** that a strict `Dag` type would make inexpressible, and baking topology into the type foists that choice on every consumer.

## Decision

One core representation: a **directed, attributed multigraph** that unconditionally permits cycles, self-loops, and parallel edges. Direction is the only first-class orientation (undirected deferred, YAGNI).

DAG-ness, simple-ness, and self-loop-freeness are **layered validators**, not baked types — structural capability (what the store can hold) is separated from validation policy (what a given graph rejects). Enforcement is available both as always-on advisory queries (cycle detection, "would this edge create a cycle?") and as opt-in, off-by-default hard mutation-time validators; the library ships three: acyclicity, simple-graph, no-self-loops.

Edges are **first-class, independently-identified entities**, so endpoint-pair access (`GetEdges(a, b)`) is a collection-returning query.

## Consequences

- A graph that is "mostly a DAG but for one edge" is directly expressible; strictness is a choice a consumer opts into, not a type they are locked into.
- First-class edges make parallel edges natural but mean there is no single "the edge from A to B" — callers query a collection. This is load-bearing for the traversal API ([ADR 0004](./0004-layered-traversal-api-extensions.md)) and storage ([ADR 0002](./0002-incidence-list-storage-slotmap-backing.md)).
- A hard structural validator that must tolerate a known exception (the cyclic self-reference) needs an exemption mechanism — deferred as a future concern, noted in the spec's fog.

Originating decision: wayfinder map [#1](https://github.com/larced/pipe/issues/1), ticket [#2](https://github.com/larced/pipe/issues/2).
