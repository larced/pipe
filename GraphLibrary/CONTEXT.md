# Graph Library

A domain-agnostic, in-memory library providing a mutable directed multigraph, a traversal/query API over it, and a separate rule-evaluation engine for validating configurations built on top of it. It knows nothing about distributions, products, or their rules (see [ADR 0001](../docs/adr/0001-project-decomposition.md)); the domain layer (Catalog) supplies those.

The design decisions behind this vocabulary — and the alternatives rejected — live in [`docs/adr/`](./docs/adr/).

## Language

### Core structure

**Graph**:
The library's core structure: a single directed, attributed multigraph that unconditionally permits cycles, self-loops, and parallel edges (canonical type `Graph<TNode,TEdge>`). Constrained shapes are expressed as Validators, not distinct types.
_Avoid_: DiGraph, DAG, network, digraph type

**Node**:
A vertex in the graph, identified by a Handle and carrying a `TNode` Payload.
_Avoid_: vertex, point

**Edge**:
A first-class, independently-identified connection from a source Node to a target Node, carrying a `TEdge` Payload. Because edges are first-class, two nodes may have many edges between them, so endpoint-pair access returns a collection.
_Avoid_: link, arc, connection, relationship (reserve "relationship" for the domain layer)

**Payload**:
The caller-supplied, compile-time-typed data attached to a Node (`TNode`) or Edge (`TEdge`). Opaque to the library and never used as identity.
_Avoid_: attribute bag, properties, attributes, data

**Validator**:
An opt-in, off-by-default constraint that rejects a mutation which would violate a chosen topology (acyclicity, simple-graph, no-self-loops). The core structure holds any topology; a validator is what makes a given graph refuse one. Distinct from a Rule, which governs a Selection rather than graph structure.
_Avoid_: constraint (reserve for rule-eval), schema, invariant

### Identity

**Handle**:
The stable, opaque identity of a Node or Edge (`NodeHandle` / `EdgeHandle`), independent of its Payload and unaffected by payload mutation. The value callers hold and pass; carries a runtime guard against cross-graph and stale use.
_Avoid_: id, key, reference, index, pointer

**Secondary index**:
An opt-in `SecondaryIndex<TKey>` mapping a payload-derived key back to Handles, for content-based lookup. Off by default and not part of core identity.
_Avoid_: lookup table, map, dictionary

### Traversal & query

**Readable graph**:
The lean read surface (`IReadableGraph<TNode,TEdge>`: nodes, edges, in/out Incidence, degree) that both traversal and rule-evaluation consume.
_Avoid_: view (reserve for Selection view), reader, read model

**Incidence**:
The in-edges and out-edges recorded per Node — the primitive that both directions of traversal walk.
_Avoid_: adjacency (implies neighbour nodes; incidence is over edges), neighbours

**Reachability**:
The set of Nodes reachable from a start Node by following Edges; returned as a node set.
_Avoid_: connectivity, closure

**Path**:
A route between two Nodes expressed as a sequence of Edge Handles — unambiguous under parallel edges.
_Avoid_: route, walk, trail, chain

**Traversal**:
A lazy, pull-based walk of the graph in a chosen order, optionally steered by a visitor returning `Continue` / `SkipDescendants` / `Stop`.
_Avoid_: iteration, search, crawl

### Rule evaluation

**Rule**:
A validity constraint over a Selection (not over graph topology — that is a Validator). Rules compose flat, conjunctive, and order-free; the extension point is `IRule`.
_Avoid_: constraint, validator, policy, requirement

**Violation**:
A single finding a Rule reports, classified as **lower-bound** ("still needs X") or **upper-bound / exclusion** ("too many / conflicts"). This classification is what lets validity and Availability derive uniformly.
_Avoid_: error, failure, issue

**Evaluator**:
The engine that, over `(graph, selection, rules)`, answers whether a Selection is valid (**Check**) and what may be added next (**Availability**). Not a solver — it does not complete, enumerate, or satisfy selections.
_Avoid_: solver, resolver, validator, checker (use "Check" for the operation)

**Availability**:
The Evaluator-derived answer, per candidate, of whether adding it keeps the Selection valid: **Available** (with headroom) or **Blocked** (with reasons). The "why-not" preview.
_Avoid_: eligibility, selectability

**Gating**:
An opt-in Rule posture where a Node is unavailable until a precondition over the *current* Selection holds (strict). The default posture is **eventual** — a constraint may sit unsatisfied and the selection is invalid-until-satisfied (gentle).
_Avoid_: blocking (overloaded with Availability's Blocked), locking

### Selection & regions

**Selection**:
A mutable overlay of Instance records layered over the base Graph, representing a candidate configuration. Leaves the base graph untouched.
_Avoid_: set, configuration, choice, basket

**Instance**:
One occurrence within a Selection of a Prototype, carrying its own instance data and exactly one Region tag. Occurrences carry no edges of their own beyond their prototype's.
_Avoid_: selected node, item, element, occurrence node

**Prototype**:
The Node Handle that an Instance is an occurrence of.
_Avoid_: template, type, kind, class

**Region**:
The flat, opaque grouping label an Instance carries, partitioning a Selection for validation. Has no base-graph topology and no registry — a region exists iff some instance carries it.
_Avoid_: layer, subgraph, group, partition, namespace, tier

**Scope**:
The Region reach of a Rule: `named region`, `named set`, `each active region`, or `global`.
_Avoid_: range, target, extent

**Selection view**:
The region-aware read surface (`SelectionView`) every Rule reads: the active Regions, an Instance's region, and per-region / cross-region / global counts.
_Avoid_: context, snapshot, state
