# 0007: Flat opaque region model for the selection overlay

## Status

Accepted

## Context

A Selection needs to partition its Instances for validation (e.g. a vehicle's FrontAxle vs RearAxle), with Rules that reach a single partition, a named set of them, each of them, or the whole selection. The open questions: what a partition *is*, and how a rule targets it.

## Decision

A **Region** is a **flat, opaque, abstract grouping label** carried by an Instance:

- **Nature — abstract label, not a topological subgraph.** It carries no base-graph topology (the base graph stays lean per [ADR 0005](./0005-rule-evaluation-checker-oracle-overlay.md)); "subgraph" in the exploratory framing is dropped.
- **Identity — opaque value, no registry.** A region is a plain opaque value with equality (same ethos as [ADR 0003](./0003-opaque-handle-identity.md)'s handles); the engine owns no region registry — a region **exists iff some instance carries it**. "This region must exist and be populated" is expressed by a Rule that names the region value.
- **Lifecycle — dynamic / open.** Any region value passed to `Selection.Add` is legal and springs the region into being on first use. A **closed set of regions** is an opt-in Rule, not a library-imposed schema.
- **Membership — exactly one region per instance.** Counting stays unambiguous. Multi-dimensional grouping, if ever needed, is a separate additive tag dimension, not a multi-valued region.

**Targeting — four scopes, one primitive.** A rule's Scope is `named region`, `named set`, `each active region`, or `global`; all four compile to one engine primitive: *filter instances by a region predicate, then count.*

**Shape — flat, no hierarchy.** A rule spanning several regions **names the set**; global + set + single give three effective levels with zero tree machinery. An app wanting hierarchy encodes a path in the opaque value ("Vehicle/FrontAxle") and names sets by convention. Nesting was rejected as unproven and as forcing the engine to own a tree.

**Constraining region structure — tiered.** Region-scoped cardinality **folds into a single `Cardinality` built-in** (absorbing the prototype's separate `LayerCardinality` and `InstanceLimit`). Constraints over regions-as-entities ("≤ N active regions", "if A active then B active", the closed set) are left to **custom `IRule`** in v1; promote to built-in later if one recurs. The one firm addition to [ADR 0005](./0005-rule-evaluation-checker-oracle-overlay.md): a **region-aware `SelectionView`** — active regions, an instance's region, and per-region / cross-region / global counts — that every region rule reads.

## Consequences

- The engine owns no region tree or registry; counting stays unambiguous.
- Multi-dimensional grouping, true roll-up cardinality, and closed region sets are additive later without a library change.
- Every region rule — built-in or custom — has a sufficient substrate (`SelectionView`) to be written without a library change.

Originating decision: wayfinder map [#1](https://github.com/larced/pipe/issues/1), ticket [#9](https://github.com/larced/pipe/issues/9).
