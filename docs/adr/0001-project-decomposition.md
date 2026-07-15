# 0001: Project decomposition for the ingestion/pipeline/graph system

## Status

Accepted

## Context

The long-term goal is a system that ingests folders containing distribution and product information, runs that data through a pipeline, and populates one or more graphs encoding dependency and configuration rules — how distributions, products, and product contents can interact and be configured relative to each other.

Rather than building this as one application, the intent is to decompose it into smaller, general-purpose projects that can each be used standalone in other applications, not just this one. This decision fixes what those projects are, their responsibilities, and how they may depend on one another, before any of them is individually specified or built.

A key tension: ingestion, pipeline execution, and graph modeling are all generically useful mechanisms, but the actual domain (distributions, products, containment/configuration rules) is specific to this problem. Baking domain knowledge into the mechanism projects would foreclose reuse.

## Decision

The system decomposes into **four projects**, each its own solution:

1. **Ingestion Engine** — a generic framework for walking folder trees and running pluggable extractors over them to produce structured records. Knows nothing about distributions or products; the extraction logic that understands those is supplied externally.
2. **Pipeline Builder** — a generic, composable pipeline/stage framework for processing data (ingestion output among other things). Domain-agnostic.
3. **Graph Library** — a generic graph data structure and query/rule-evaluation library. Domain-agnostic; knows nothing about distributions, products, or their rules.
4. **Catalog** — the domain layer. Defines what a distribution and a product are, what products contain, and the dependency/configuration rules governing how they interact. Supplies the extractors that plug into Ingestion Engine, builds the pipelines (via Pipeline Builder) that process catalog data, and defines the graph schema/rules evaluated via Graph Library.

### Dependency direction

Ingestion Engine, Pipeline Builder, and Graph Library are **fully independent siblings** — none references either of the others, and none references Catalog. Each depends on nothing beyond the base platform. Catalog is the only project that depends on the other three; it is where they are wired together.

This is deliberately stricter than allowing the Pipeline Builder to ship built-in adapters for the other two (e.g. an ingestion-source stage or a graph-sink stage). Convenience adapters can be added later without breaking anything; relaxing an existing dependency after code exists is a much larger cost. Standalone reuse of Ingestion Engine and Graph Library in unrelated contexts is a stated goal, so their independence is protected.

### Solution layout

Each project is a top-level folder in this repo, not nested under a shared `src/` — a shared top-level `src/` would imply the four are parts of one larger project, which contradicts their independence. Each project folder is self-contained with its own solution, internal `src/`/`tests/`, and `docs/adr/`:

```
/
├── IngestionEngine/
│   ├── IngestionEngine.sln
│   ├── src/IngestionEngine/IngestionEngine.csproj
│   ├── tests/IngestionEngine.Tests/
│   ├── CONTEXT.md
│   └── docs/adr/
├── PipelineBuilder/
│   ├── PipelineBuilder.sln
│   ├── src/...
│   ├── CONTEXT.md
│   └── docs/adr/
├── GraphLibrary/
│   ├── GraphLibrary.sln
│   ├── src/...
│   ├── CONTEXT.md
│   └── docs/adr/
└── Catalog/
    ├── Catalog.sln
    ├── src/...
    ├── CONTEXT.md
    └── docs/adr/
```

`CONTEXT-MAP.md` at the repo root indexes each project's `CONTEXT.md`.

### Standing constraints

All four projects target **C# on .NET 10**. Primary design goals, in order: **performance, memory efficiency, ease of use**.

## Out of scope

- **The host application** — whatever process actually runs ingestion end-to-end (CLI, service, or embedding application) is not decided here. It is a future effort once the four library projects exist.
- **Catalog's internal structure** (e.g. whether it further splits into sub-projects), testing strategy, CI, and package/namespace naming conventions are implementation details for each project's own future spec effort, not decomposition-level concerns.

## Consequences

- Ingestion Engine and Graph Library can be adopted independently by unrelated projects with zero coupling to this domain.
- Building the pipeline that connects ingestion → graph for the catalog use case requires writing glue code in Catalog; there are no built-in adapters. This is accepted as the cost of strict independence.
- Each project can be versioned, tested, and released on its own schedule.
