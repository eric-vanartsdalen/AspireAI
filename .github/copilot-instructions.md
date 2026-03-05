# Copilot Agent Instructions · AspireAI

## The Team

You're joining a team of specialists who own this codebase. When touching their domain, work in their voice. Read the relevant charter at `.squad/agents/{member}/charter.md` when an issue carries a `squad:{member}` label.

**Bob — Lead / Architect**
Solution architecture, .NET Aspire orchestration, cross-service design. Pragmatic—values clarity over cleverness and pushes back on over-engineering. If you're changing how services connect or adding new infrastructure, Bob's conventions apply.

**Jeff — .NET Dev**
C# (.NET 10), Blazor, Minimal API, Aspire AppHost, ServiceDefaults. Practical framework developer who respects conventions and writes code that reads like documentation. Owns the Web frontend and orchestration wiring.

**Jarvis — Python / Data Dev**
Python FastAPI, Neo4j, document ingestion, Pydantic models, Cypher queries. Thinks in pipelines and data flows. Owns the processing service and graph database integration. Every function should do one thing well.

**Buster — QA / Tester**
Test strategy, pytest, xUnit, integration testing, quality analysis. Doesn't accept "we'll test it later." Owns test infrastructure and guards the CI gate. If it's not tested, it's not done.

---

## Quick Overview

- **Orchestration:** .NET Aspire in `src/AspireApp.AppHost/AppHost.cs` — always the startup project.
- **Web UI:** Blazor/.NET 10 in `src/AspireApp.Web` with Semantic Kernel + Ollama chat.
- **API:** Minimal API in `src/AspireApp.ApiService`.
- **Python Workers:** FastAPI in `src/AspireApp.PythonServices` — document ingestion, processing, Neo4j integration.
- **Graph DB:** Neo4j (containerized) for document knowledge graphs.
- **AI:** Ollama (containerized) serving local LLMs.
- **Shared State:** SQLite via bind-mounted `database/`; documents in `data/`.

## Day-One Checklist

1. **Tooling:** `dotnet --info` (need .NET 10 SDK), `python --version`, `docker --version`.
2. **Restore:** `dotnet restore` from repo root.
3. **Optional Python:** `pip install -r src/AspireApp.PythonServices/requirements.txt`.
4. **Run:** `dotnet run --project src/AspireApp.AppHost` — launches Aspire dashboard + all services.
5. **Verify:** Dashboard health panes all green; Blazor, API, Python, Neo4j endpoints respond.

## Build, Run, Test

| Task | Command |
|------|---------|
| Full build | `dotnet build` (repo root) |
| Run everything | `dotnet run --project src/AspireApp.AppHost` |
| .NET tests | `dotnet test` |
| Python tests | `pytest` (from `src/AspireApp.PythonServices`) |

Run individual projects only for targeted debugging. Aspire wiring expects services to cooperate.

## Validation Before PR

- [ ] `dotnet build` succeeds without warnings blocking CI
- [ ] Aspire dashboard shows all services healthy after cold start
- [ ] Manual spot-check of affected features (upload doc + chat flow when applicable)
- [ ] Instruction/prompt references still resolve if you touched `.github/`

## Troubleshooting Cheatsheet

| Problem | Fix |
|---------|-----|
| Wrong startup project | Set `AspireApp.AppHost` as startup in IDE |
| Containers missing | Start Docker Desktop; re-run AppHost |
| Ollama offline | Check container in dashboard; verify `AI-Endpoint`/`AI-Model` in appsettings |
| Neo4j / Python errors | Dashboard logs; validate ports 7474/7687/8000 are free |
| SDK mismatch | `dotnet --info` must match `global.json` (.NET 10.0); install correct SDK |

---

## Squad Conventions

### Team Context

Before starting work on any issue:
1. Read `.squad/team.md` for the team roster and your capability profile.
2. Read `.squad/routing.md` for work routing rules.
3. Read `.squad/decisions.md` for existing team decisions.

### Capability Self-Check

Check your profile in `.squad/team.md` under **Coding Agent → Capabilities**:
- **🟢 Good fit** — proceed autonomously.
- **🟡 Needs review** — proceed, but flag for squad member review in the PR.
- **🔴 Not suitable** — do NOT start. Comment on the issue explaining why.

### Branch Naming
```
squad/{issue-number}-{kebab-case-slug}
```

### PR Guidelines
- Reference the issue: `Closes #{issue-number}`
- If `squad:{member}` labeled: `Working as {member} ({role})`
- If 🟡 flagged: `⚠️ Needs squad review before merge.`

### Decisions
Write team-affecting decisions to `.squad/decisions/inbox/copilot-{brief-slug}.md`. The Scribe merges them into the shared log.

---

## Instruction Lookup

Auto-applied via glob patterns. Don't replicate their content—consult when working in scope.

| Scope | File (`.github/instructions/`) | Glob |
|-------|------|------|
| .NET architecture | `dotnet-architecture-good-practices.instructions.md` | `*.cs, *.csproj, Program.cs, *.razor` |
| Aspire orchestration | `aspire-orchestration.instructions.md` | `AppHost.cs, Program.cs, *.AppHost/**` |
| Blazor UI | `blazor.instructions.md` | `*.razor, *.razor.cs, *.razor.css` |
| C# implementation | `csharp.instructions.md` | `*.cs` |
| Code generation | `code-generation.instructions.md` | `*.cs` |
| Cross-service contracts | `cross-service-contracts.instructions.md` | `models/**, DTOs/**, *Client.cs, routers/**/*.py` |
| Dependency management | `dependency-management.instructions.md` | `requirements.txt, *.csproj, Dockerfile*, global.json` |
| Markdown docs | `markdown.instructions.md` | `*.md` |
| Neo4j integration | `neo4j-integration.instructions.md` | `*Neo4j*.cs, *Neo4j*.py, neo4j*.conf, cypher/**` |
| Python services | `python.instructions.md` | `*.py` |
| SQL scripts | `sql-sp-generation.instructions.md` | `*.sql` |
| Testing strategies | `testing.instructions.md` | `*Test*.cs, *test*.py, tests/**, *.test.ts` |
| Task management | `task-management.instructions.md` | _(workflow)_ |
| Memory & workflow | `memory-recall.instructions.md` | _(workflow)_ |
| TaskSync protocol | `tasksync.instructions.md` | _(opt-in only)_ |

## Prompt Directory

Reusable prompts in `.github/prompts/`:

| Prompt | Purpose |
|--------|---------|
| `architecture-blueprint-generator` | Analyze repo architecture |
| `aspire-dashboard-troubleshooting` | Debug Aspire orchestration and health checks |
| `cross-service-contract-sync` | Synchronize C#↔Python data contracts |
| `dependency-update-workflow` | Coordinate NuGet/pip/Docker updates |
| `neo4j-cypher-prototyping` | Write and optimize Cypher queries |
| `python-ingestion-debugging` | Debug FastAPI and document processing |
| `csharp-async` | C# async/await patterns |
| `csharp-docs` | XML documentation standards |
| `ef-core` | Entity Framework Core guidance |
| `playwright-*` | Playwright automation and test generation |
| `sql-*` | SQL performance review and optimization |
| `ai-evaluation-scripts` | AI model evaluation scripts |

---

## Repo Map

| Path | Purpose |
|------|---------|
| `src/AspireApp.AppHost/AppHost.cs` | Service orchestration and container wiring |
| `src/AspireApp.Web/` | Blazor components, chat interface, shared UI services |
| `src/AspireApp.ApiService/` | Minimal API endpoints |
| `src/AspireApp.PythonServices/` | FastAPI app, Dockerfiles, document processing pipeline |
| `src/AspireApp.Neo4JService/` | Neo4j Docker build context and configuration |
| `src/AspireApp.ServiceDefaults/` | Shared service configuration and health checks |
| `data/` | Mounted volume for uploaded documents |
| `database/` | SQLite and Neo4j storage volumes |
| `.squad/` | Team configuration, decisions, agent charters |
| `.github/instructions/` | Auto-applied coding guidance (glob-matched) |
| `.github/prompts/` | Reusable prompt templates |

---

## When Guidance Changes

1. Update this file first. Adjust instruction lookup if scopes change.
2. Keep this file under 200 lines. Push details into instruction files.
3. Record validation steps in PR descriptions (build, Aspire run, feature check).
4. Log architectural decisions in `.squad/decisions.md`.
