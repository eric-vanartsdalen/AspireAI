# Copilot Agent Instructions · AspireAI

You are a brilliant coding expert developer of Python, C#, Blazor, Javascript and SQL including architectual and designs. (both code and database) You write direct, concise and readable code. You avoid over-engineering. Follow the instructions below carefully.

Last updated: 2025-11-02

## Quick Overview
- Aspire-hosted orchestration lives in `src/AspireApp.AppHost/AppHost.cs` and must be the startup project every time.
- Web UI is Blazor/.NET 9 (`src/AspireApp.Web`); API is minimal API in `src/AspireApp.ApiService`.
- Python FastAPI workers (`src/AspireApp.PythonServices`) ingest documents and talk to Neo4j.
- Ollama and Neo4j run as containers; shared state is bind-mounted from `data/` and `database/`.

## Day-One Checklist
1. Tooling sanity: `dotnet --info`, `python --version`, `docker --version`.
2. Restore: `dotnet restore` (repo root).
3. Optional local Python setup: `pip install -r src/AspireApp.PythonServices/requirements.txt`.
4. Run everything: `dotnet run --project src/AspireApp.AppHost` (launches Aspire dashboard and all services).
5. Verify services: dashboard health panes all green; API, Blazor, Python, and Neo4j endpoints respond.

## Build, Run, Test
- Full build: `dotnet build` from repo root.
- Orchestration run: `dotnet run --project src/AspireApp.AppHost` (preferred) or Visual Studio with `AspireApp.AppHost` set as startup.
- Targeted troubleshooting: only run individual projects when debugging locally (Blazor, API, Python). Remember Aspire wiring expects services to cooperate.
- Tests: `dotnet test` (when projects expose suites); Python tests live under `src/AspireApp.PythonServices` (pytest).

## Validation Before PR
- Build succeeds without warnings blocking CI.
- Aspire run is stable; dashboard shows all services healthy after cold start.
- Manual spot check of affected features (upload doc + chat flow when applicable).
- Update instructions or prompts only after confirming links, globs, and references resolve.

## Troubleshooting Cheatsheet
- Startup project wrong → set `AspireApp.AppHost` as startup in IDE.
- Containers missing → start Docker Desktop; re-run AppHost.
- Ollama offline → confirm container status in dashboard, ensure `AI-Endpoint`/`AI-Model` in appsettings match desired model.
- Neo4j or Python errors → inspect dashboard logs; validate ports 7474/7687/8000 are free.
- SDK mismatch → `dotnet --info` must align with `global.json`; install correct SDK otherwise.

## Instruction Lookup
| Scope | File | Notes |
|-------|------|-------|
| .NET architecture, `.csproj`, Razor | `instructions/dotnet-architecture-good-practices.instructions.md` | Clean Architecture focus; DDD optional. Updated 2025-11-02. |
| Aspire orchestration, `AppHost.cs` | `instructions/aspire-orchestration.instructions.md` | Service registration, health checks, volumes, dependencies. New 2025-11-02. |
| Neo4j integration, graph patterns | `instructions/neo4j-integration.instructions.md` | Schema design, Cypher queries, Python driver usage. New 2025-11-02. |
| Blazor UI (`*.razor*`) | `instructions/blazor.instructions.md` | Component patterns, state guidance. |
| C# implementation details (`*.cs`) | `instructions/csharp.instructions.md` | Style, async, testing rules. |
| Python services (`*.py`) | `instructions/python.instructions.md` | FastAPI, typing, formatting. |
| SQL scripts | `instructions/sql-sp-generation.instructions.md` | Stored procedure authoring rules. |
| Markdown docs | `instructions/markdown.instructions.md` | General repo docs; front matter optional unless target system needs it. |
| Memory & workflow | `instructions/memory-recall.instructions.md` | Lightweight session routine; create memory files only on request. |
| TaskSync protocol | `instructions/tasksync.instructions.md` | Optional terminal loop—enable only when the user asks for TaskSync mode. |

## Prompt Directory Snapshot
- `prompts/architecture-blueprint-generator.prompt.md` – analyze repo architecture, generate documentation.
- `prompts/csharp-async.prompt.md` – assist with async patterns in C#.
- `prompts/csharp-docs.prompt.md` – produce C# API documentation.
- `prompts/ef-core.prompt.md` – Entity Framework Core guidance.
- `prompts/playwright-*.prompt.md` – Playwright automation exploration/test generation.
- `prompts/sql-*.prompt.md` – SQL performance review and optimization.
*(Plan to add Aspire/Neo4j/Python ingestion prompts in WS3.)*

## Tasks & Memory Notes
- `.github/tasks/` includes template files (`feature`, `bug`, `research`) plus `_index.md`; duplicate a template and update the index when formal tracking is required.
- Memory-bank files live in `.github/memory-bank/`; update them only when the user requests persistent context, and record `Last reviewed` timestamps when you do.

## Repo Map (useful starting points)
- `README.md` – contributor primer + troubleshooting basics.
- `src/AspireApp.AppHost/AppHost.cs` – service orchestration and container wiring.
- `src/AspireApp.Web/` – Blazor components, shared UI services, static assets.
- `src/AspireApp.ApiService/` – Minimal API endpoints.
- `src/AspireApp.PythonServices/` – FastAPI app, Dockerfiles, ingestion scripts.
- `src/AspireApp.Neo4JService/` – Neo4j Docker build contexts.
- `data/` & `database/` – mounted volumes for documents and graph storage.

## When Guidance Changes
- Update this file first. Note new facts and adjust glob-to-instruction mapping.
- Confirm instructions remain under 400 lines; trim duplication with README.
- Record validation steps in PR descriptions (build, Aspire run, feature check).
