# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`CONTEXT-MAP.md`** at the repo root — it points at one `CONTEXT.md` per context (per C# project). Read each one relevant to the topic.
- **`docs/adr/`** — system-wide decisions. Also check `<Project>/docs/adr/` for context-scoped decisions.

If any of these files don't exist, **proceed silently**. Don't flag their absence; don't suggest creating them upfront. The `/domain-modeling` skill (reached via `/grill-with-docs` and `/improve-codebase-architecture`) creates them lazily when terms or decisions actually get resolved.

## File structure

This repo is a monolith housing multiple independent, generalized C# projects — a multi-context layout. Each project is a **top-level folder**, not nested under a shared `src/` (a shared top-level `src/` would imply the projects are parts of one larger whole, which contradicts their independence — see [ADR 0001](../adr/0001-project-decomposition.md)):

```
/
├── CONTEXT-MAP.md
├── docs/adr/                          ← system-wide decisions
├── <ProjectA>/
│   ├── <ProjectA>.sln
│   ├── src/...
│   ├── CONTEXT.md
│   └── docs/adr/                      ← project-specific decisions
└── <ProjectB>/
    ├── <ProjectB>.sln
    ├── src/...
    ├── CONTEXT.md
    └── docs/adr/
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in the relevant `CONTEXT.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal — either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders) — but worth reopening because…_
