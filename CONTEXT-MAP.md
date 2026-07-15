# Context Map

This repo houses multiple independent, general-purpose C# projects (see [ADR 0001](docs/adr/0001-project-decomposition.md)). Each has its own `CONTEXT.md` and `docs/adr/`, scoped to that project's domain.

| Project | Responsibility | Context |
| --- | --- | --- |
| Ingestion Engine | Generic folder-walking + pluggable-extraction framework | `IngestionEngine/CONTEXT.md` (not yet written) |
| Pipeline Builder | Generic composable pipeline/stage framework | `PipelineBuilder/CONTEXT.md` (not yet written) |
| Graph Library | Generic graph data structure + query/rule-evaluation library | [`GraphLibrary/CONTEXT.md`](GraphLibrary/CONTEXT.md) |
| Catalog | Domain layer: distributions, products, containment/configuration rules | `Catalog/CONTEXT.md` (not yet written) |

Each `CONTEXT.md` is written lazily, as that project's own spec effort resolves its terms and decisions (see `/domain-modeling`).
