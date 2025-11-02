# Copilot Agent Instructions - AspireApp

**Updated 2025-11-02:** Consolidated `taming-copilot.instructions.md` and `memory-bank.instructions.md` into `memory-recall.instructions.md` for simplicity. Updated `tasksync.instructions.md` to support asynchronous task execution to avoid blocking main threads, while maintaining atomic consistency. Noted DDD as legacy in `dotnet-architecture-good-practices.instructions.md`, recommending Clean Architecture as the current industry standard for better alignment with modern .NET practices and Aspire orchestration.

You are a brilliant coding expert assistant in 
Python, C#, Blazor, Javascript and SQL
including architectual and designs. (both code and database)
You write direct, concise and readable code.
Follow the instructions below carefully.

---

## 1) This repo details

This repository hosts a **.NET Aspire AppHost** orchestration used as a learning/demo platform:

* **Blazor Web UI** frontend (C#/.NET 9/10) for chat, document upload, and AI interaction demos
* **Python FastAPI** backend (Python 3.12, Dockerized via Aspire) for document ingestion and Graph-RAG experiments *(work in progress)*
* **Ollama** LLM integration (containerized, managed by Aspire)
* **Neo4j** graph database (containerized via Dockerfile) *(work in progress)*

* **Key architecture**: Uses **.NET Aspire AppHost** (`src/AspireApp.AppHost/AppHost.cs`) for orchestration-**not docker-compose**. Services are defined via `AddDockerfile()` and `AddProject<>()` in the AppHost.

Primary goals: iterate on RAG document flows, maintain modular/configurable services, keep local dev reproducible via Aspire.

---

## 2) High-level repo facts (read first)

* **Languages**: C# (.NET 9/10 / Blazor), Python 3.12 (FastAPI), Docker, PowerShell/bash for scripts
* **Project type**: Aspire-orchestrated multi-service app (Blazor UI + .NET API + Python services + containerized LLM/Neo4j)
* **Orchestration**: Aspire AppHost (`src/AspireApp.AppHost/AppHost.cs`) - startup project must be `AspireApp.AppHost`, not individual services
* **Size**: Small-to-medium learning project (multiple .NET projects + Python service + Dockerfiles)

**Critical**: Always run via `AspireApp.AppHost` project (F5 in Visual Studio or `dotnet run --project src/AspireApp.AppHost`). Do NOT run individual services directly unless debugging specific issues.

---

## 3) Trusted bootstrap & environment setup (do these **always** before code changes)

1. **Confirm local toolchain and SDKs:**
   * `dotnet --info` - verify SDK matches version in `global.json` (if present)
   * `python --version` - verify Python 3.12+ (if developing Python services locally outside Docker)
   * `docker --version` - ensure Docker Desktop or Engine is running *(required for Aspire)*

2. **Bootstrap repo dependencies (run from repo root):**
   * `dotnet restore` - always run before build
   * *(Optional)* Python local dev: `pip install -r src/AspireApp.PythonServices/requirements.txt` if editing/debugging Python services outside Docker

3. **Confirm Docker is running:**
   * Aspire AppHost automatically orchestrates Ollama (local by default), Neo4j, and Python services via Docker
   * **Ollama configuration**: By default, Aspire manages a local containerized Ollama. To use a remote Ollama service, configure `AI-Endpoint` in `appsettings.json` or environment variables
   * No manual docker-compose or service startup needed-just run `AspireApp.AppHost` (F5 or `dotnet run --project src/AspireApp.AppHost`)

---

## 4) Build / run / test commands (recommended order)

These are the canonical steps the agent should follow for any PR change:

**Setup for new feature work**
* `git checkout main && git pull`
* `git checkout -b feature/<description>`
* `dotnet restore` (run from solution root�uses `.sln` file)

**Build**
* `dotnet build` (builds entire solution from root)
* Python services build automatically when Aspire AppHost runs

**Run locally (dev)**
* **Primary method (Aspire orchestration)**:
  * Visual Studio: Set `AspireApp.AppHost` as startup project ? F5
  * CLI: `dotnet run --project src/AspireApp.AppHost`
  * This launches Aspire Dashboard and orchestrates all services (Blazor UI, API, Python, Ollama, Neo4j)
* **Individual service debugging**: Only run individual projects directly when troubleshooting specific service issues

**Test & Validation** *(future: currently in development)*
* Unit/integration tests: `dotnet test` (run from solution root)
* Python API tests: `pytest` from `src/AspireApp.PythonServices/` (when implemented)
* UI tests: Playwright tests (when implemented)

**Linting / Static analysis** *(aspirational: not yet configured)*
* .NET: `dotnet format` (if `.editorconfig` configured)
* Python: `ruff check .` or `flake8 .` (if linting configured in requirements.txt)

---

## 5) Local validation before PR (CI: future)

**Pre-PR checklist** *(No CI currently-validate locally)*:

1. `dotnet build` succeeds from solution root
2. Run `AspireApp.AppHost` and verify:
   - Aspire Dashboard launches successfully
   - All services start without errors (check dashboard status)
   - Health endpoints return 200 (visible in dashboard)
3. Manually test affected functionality
4. Code follows existing style conventions

**Future CI plans**: GitHub Actions workflow for automated build/test validation

**PR best practices**: Keep changes focused and well-scoped. Include clear descriptions with validation steps performed.

---

## 6) Project layout guidance (where to look first)

**Key directories and files**:
* `README.md` - Project overview, prerequisites, troubleshooting
* `src/AspireApp.AppHost/` - **Aspire orchestration** (AppHost.cs defines all services) **- MUST be startup project**
* `src/AspireApp.Web/` - **Blazor frontend** (Razor components, chat UI, file upload)
* `src/AspireApp.ApiService/` - .NET minimal API service
* `src/AspireApp.ServiceDefaults/` - Shared Aspire service configurations
* `src/AspireApp.PythonServices/` - Python FastAPI backend, Dockerfiles, requirements.txt
* `src/AspireApp.Neo4JService/` - Neo4j Dockerfiles
* `database/` - **Shared data persistence** (SQLite DB storage, Neo4j backups, bind-mounted volumes)
* `data/` - **Document storage** (uploaded files for processing, bind-mounted to Python service)
* `roadmap/` - Project roadmap and planning docs
* `.github/` - This instructions file, future CI workflows

**Shared data directories** (`database/`, `data/`):
* These are bind-mounted volumes shared between Aspire-orchestrated services
* Used for local persistence and cross-service data exchange
* Future consideration: migrate from SQLite to mainstream DB, centralize document storage

**When searching for functionality**:
* Service wiring/orchestration - `src/AspireApp.AppHost/AppHost.cs`
* UI components - `src/AspireApp.Web/Components/`
* Python API endpoints - `src/AspireApp.PythonServices/`
* Configuration - `appsettings.json` files in each project
* Dockerfiles - Service-specific directories (PythonServices, Neo4JService)

**Critical startup issue**: If pulling repo fresh, Visual Studio may default to `AspireApp.ApiService` as startup project. **Always verify `AspireApp.AppHost` is set as startup project** (bold in Solution Explorer) before running.

**If an instruction references a missing file**, search for similar patterns: `Dockerfile`, `requirements.txt`, `appsettings.json`, `Program.cs`.

---

## 7) Agent behaviour rules & PR hygiene (must follow)

* **Always** run full build and validation locally before proposing a PR
* Keep changes focused and well-scoped
* If changes affect multiple services, run `AspireApp.AppHost` to validate all service interop
* Do not add secrets or credentials-use `.env.example` pattern for config samples
* Follow existing coding style and conventions
* Write clear commit messages and PR descriptions listing validation steps performed
* When editing Dockerfiles or AppHost.cs service definitions, verify:
  - Docker build context paths are correct
  - Container health checks pass
  - No unintended image size increases

---

## 8) Troubleshooting common failures (quick reference)

* **Wrong startup project**: Verify `AspireApp.AppHost` is set as startup project (bold in Solution Explorer). If Visual Studio defaults to `AspireApp.ApiService`, right-click `AspireApp.AppHost` - "Set as Startup Project"

* **Docker not running**: Ensure Docker Desktop or Engine is running before starting AppHost. Check `docker ps` works. Use `docker system prune` to free space if builds fail due to disk.

* **Ollama not reachable**: 
  - Verify Aspire Dashboard shows Ollama container running
  - Check `AI-Endpoint` and `AI-Model` configuration in `appsettings.json` or environment variables
  - For remote Ollama: ensure endpoint URL is accessible and model is pulled

* **Services failing health checks**: 
  - Check Aspire Dashboard - Logs tab for specific service errors
  - Verify Docker containers are running (check dashboard status)
  - Check port conflicts: ensure no other services using ports 7474, 7687, 8000, etc.

* **SDK version mismatch**: Run `dotnet --info` to verify .NET 9 or 10 SDK is installed. Check `global.json` for required version. Reinstall SDK if version is incorrect.

* **Python service build failures**: Check `src/AspireApp.PythonServices/requirements.txt` dependencies. Verify Docker BuildKit is enabled.

---

## 9) When to search the repo (and when not to)

* Trust this file for standard processes. Only run a repo-wide search when:
  * A required file referenced here is missing
  * You need exact environment variable names or a specific workflow name
  * A build error references a file or step not documented here

**Useful search patterns**:
* Aspire orchestration: `AppHost.cs`, `AddDockerfile`, `AddProject`, `WithReference`
* Configuration: `appsettings.json`, `AI-Endpoint`, `AI-Model`
* Docker: `Dockerfile`, `requirements.txt`
* Build/test: `pytest.ini`, `.editorconfig`, `global.json`
* Git: `.github/workflows`

---

*If you find any of the above is inaccurate for this repository, update this file and add a brief note at the top. Otherwise, trust these instructions first and only search the repo when you need a precise path or configuration name.*

*End of copilot onboarding guidance.*

Last updated: 2025-11-02