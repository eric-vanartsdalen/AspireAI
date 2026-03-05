# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, design, strategy | Bob | Solution structure, service boundaries, tech decisions |
| C#, .NET, Blazor, Minimal API | Jeff | AppHost, API endpoints, Razor components, ServiceDefaults |
| Aspire orchestration | Jeff | AppHost.cs, container wiring, health checks |
| Python, FastAPI, document processing | Jarvis | Ingestion workers, FastAPI routes, Pydantic models |
| Neo4j, graph database, Cypher | Jarvis | Schema design, queries, driver patterns |
| Ollama / AI integration | Jarvis + Jeff | Python-side AI calls + C# SemanticKernel |
| Testing, quality, edge cases | Buster | Test coverage, test strategy, quality analysis |
| Code review | Bob | Review PRs, check quality, architecture alignment |
| Scope & priorities | Bob | What to build next, trade-offs, roadmap decisions |
| Cross-service contracts | Bob + Jeff + Jarvis | C#↔Python DTOs, API shapes, serialization |
| Async issue work | @copilot 🤖 | Well-defined tasks matching capability profile |
| Session logging | Scribe | Automatic — never needs routing |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, evaluate @copilot fit, assign `squad:{member}` label | Bob |
| `squad:bob` | Architecture, design, review | Bob |
| `squad:jeff` | .NET, Blazor, Aspire, C# work | Jeff |
| `squad:jarvis` | Python, Neo4j, data pipeline work | Jarvis |
| `squad:buster` | Testing, QA, quality work | Buster |
| `squad:copilot` | Assign to @copilot for autonomous work (if enabled) | @copilot 🤖 |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. Bob handles all `squad` (base label) triage.
8. **@copilot routing** — when evaluating issues, check @copilot's capability profile in `team.md`. Route 🟢 good-fit tasks to `squad:copilot`.
