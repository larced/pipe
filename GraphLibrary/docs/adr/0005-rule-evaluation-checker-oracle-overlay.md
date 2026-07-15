# 0005: Rule evaluation as a checker + availability oracle over a selection overlay

## Status

Accepted

## Context

The library must validate configurations built over a Graph — "is this selection valid, and why not; what can I add next, and why not" — without churning the base graph or entangling validity with topology.

## Decision

The rule layer is a **checker + availability oracle, not a solver** — auto-completion, satisfiability, and solution-enumeration are deferred (the classified-violation abstractions are what a future solver would consume).

- **Selection state is an overlay, not graph mutation.** A Selection is a separate overlay of Instance records `(prototype handle, region tag, instance data)`, leaving the base graph lean and untouched. Instances are **occurrences** with no edges of their own beyond their prototype's, so instance data is free of `TNode`.
- **Rules are a separate, handle-referencing set** (never node payload — keeping "rule-eval is a distinct concern" physically true), composed **flat / conjunctive / order-free**; the Evaluator unions the Violations. No precedence, no short-circuit.
- **The extension point is a classified `Check(view) → violations` predicate** (`IRule`); each Violation is tagged **lower-bound** ("still needs X") or **upper-bound / exclusion** ("too many / conflicts"). That single classification makes validity *and* Availability derive uniformly across built-in and custom rules.
- **Availability is engine-derived**, not per-rule-implemented: simulate adding each candidate, re-run `Check`, and block only on **newly-caused upper-bound / exclusion** violations — attributed by violation **identity, not count** (pushing an already-full cardinality further is a fresh breach even though the count is unchanged). This yields a "why-not" preview across custom rules for free.
- **Gating is opt-in.** A rule may declare a node's precondition over the *current* selection (strict); the default posture is gentle/eventual (add freely, invalid-until-satisfied). Availability = derived-blocks ∪ gate-failures.
- **Edges feed rules but are not rules (option C).** Edges are domain topology you traverse; a rule *may read* them — e.g. a built-in `RequiresEdge` enforcing an AND-dependency for every `requires`-typed edge — while OR-groups, conflict-groups, cardinality, and instance-limits stay declarative rule objects over handles (they are hyper-edge / group-shaped and do not fit `TEdge` honestly).

## Consequences

- The base graph never churns through selection activity — traversal, validators, and storage are undisturbed, and instance data carries no `TNode`.
- "Why not available" works for arbitrary custom rules with no extra author code.
- The design was validated with a side-by-side logic prototype (branch [`prototype/rule-eval-instances`](https://github.com/larced/pipe/tree/prototype/rule-eval-instances)), which confirmed the overlay and a core-native representation project the same view — so the choice is structural, and the overlay wins on base-graph cleanliness.
- The exemption/escape-hatch for hard *structural* validators ([ADR 0001](./0001-single-multigraph-core-layered-validators.md)) may reuse this pattern but is a separate evaluation domain (over topology, not a selection).

Originating decision: wayfinder map [#1](https://github.com/larced/pipe/issues/1), ticket [#3](https://github.com/larced/pipe/issues/3).
