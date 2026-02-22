# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2025-02-22 — Initial Quality Audit
- **Zero automated tests exist.** All 6 "test" files are manual diagnostic/benchmark scripts with no assertions and no pytest/xUnit integration.
- **CI is non-functional.** `squad-ci.yml` echoes a placeholder string — no build, no test execution.
- **.NET build succeeds** (when Aspire is not running). SDK is .NET 10.0 Preview. 5 projects: AppHost, ApiService, Web, ServiceDefaults, Neo4JService.
- **Python has no test infrastructure.** No `pytest` in requirements.txt, no conftest.py, no pytest.ini.
- **Python dependencies are unpinned** — no version numbers in requirements.txt.
- **C# logging inconsistency.** Chat.razor.cs uses 35+ `Console.WriteLine` calls instead of `ILogger`. Multiple other files do the same.
- **Broad exception catching** everywhere — 9+ `catch(Exception)` in C#, 18+ `except Exception` in Python. Most return generic error messages.
- **Legacy schema debt.** `DocumentEntities.cs` contains deprecated `Document` and `ProcessedDocument` classes with a TODO to remove.
- **Cross-service contract risk.** C# uses `FileMetadata` with EF Core column mappings; Python uses `Document` Pydantic model with different field names. No contract tests verify alignment.
- **OllamaWarmupService** creates `new HttpClient()` directly instead of using `IHttpClientFactory`.
- **Global FastAPI exception handler** returns raw exception strings — potential information leak.
- **Python DatabaseService** is re-instantiated per request via `get_database_service()` dependency, re-running schema checks each time.
- **Highest-risk untested areas:** FileUploadController (validation, security), FileStorageService (data integrity), Python DatabaseService (all CRUD), FastAPI routes (all 3 routers).

### 2026-02-21 — Cross-Agent Findings

**From Keaton:**
- Processing pipeline blocked by ~10 missing DatabaseService methods in Python
- Status casing bug ("Uploaded" vs "uploaded") prevents file discovery
- ApiService vestigial, should be removed

**From Fenster:**
- LightRAG and Ollama have no health checks, causing webfrontend to block indefinitely
- Config key mismatch: AI-Chat-Model (AppHost) vs AI-Model (Web services)
- SemanticKernel version skew (1.71.0 vs 1.68.0-alpha connector)

**From McManus:**
- Save_document_page() signature mismatch will crash during processing
- FK column name conflict creates data integrity risk
- Requirements.txt unpinned — reproducibility issue

### 2026-02-22 — Squad Orchestration Complete

**Status:** All four agents completed independent reviews; findings merged into shared decisions.md.

**Hockney's Test Roadmap (Phase 1 Gates P0/P1 Fixes):**

**Phase 1 (Week 1): Foundation — **CRITICAL BLOCKER** for merges**
- Create `AspireApp.UnitTests.csproj` (xUnit)
- Add `pytest` + `conftest.py` to Python services
- Update CI (`squad-ci.yml`): run `dotnet build`, `dotnet test`, `pytest`
- **Effort:** 4 hours
- **Blocks:** No PR merges without CI passing

**Phase 2 (Week 2): High-Risk Paths**
- Contract tests: C# ↔ Python JSON serialization validation
- FileUploadController validation tests
- Python router unit tests (mocked DatabaseService)
- Status casing verification ("uploaded" lowercase)
- **Effort:** 22 hours
- **Blocks:** No model refactoring without contract tests

**Phase 3 (Week 3): Integration Suite**
- End-to-end: file upload → processing → retrieval
- Python DatabaseService integration (real SQLite)
- Cross-service E2E (real Neo4j)
- **Effort:** 12 hours

**Phase 4 (Week 4+): Edge Cases & Stress**
- Concurrent uploads, large files, timeouts, cleanup

**Dependency:** P0 code fixes (Fenster + McManus) must land and pass manual validation before Phase 2 starts.

### 2026-02-22 — Test Posture Review & Plan

**Key Findings:**
- **Zero automated tests.** No xUnit projects, no pytest integration. 6 files named `test_*.py` are diagnostic scripts with no assertions.
- **CI is broken.** `squad-ci.yml` echoes placeholder; no build verification, no test runs, no PR gating.
- **Contract misalignment risk is CRITICAL.** C# (`FileMetadata`) ↔ Python (`Document`) have no JSON serialization tests. Field renames or type changes will crash Python at runtime silently.
- **Cross-service testing completely absent.** 0 tests verify C# JSON serialization matches Python Pydantic deserialization.
- **High-risk paths untested:** File upload validation, processing pipeline, error handling, concurrent access.
- **Python dependencies unpinned.** Reproducible builds impossible; docling updates could break silently.

**Test Gap Priorities:**
1. Phase 1 (Week 1): Test infrastructure + CI pipeline (xUnit project, pytest, conftest, CI workflow) — **4h effort**
2. Phase 2 (Week 2): Contract tests + controller tests + router unit tests — **22h effort**  
3. Phase 3 (Week 3): Integration suite (end-to-end file upload → processing → retrieval) — **12h effort**
4. Phase 4 (Week 4+): Stress/edge case tests (concurrent, large files, timeouts, cleanup)

**File Paths (Key Components):**
- C# projects: `src/AspireApp.Web`, `src/AspireApp.ApiService`, `src/AspireApp.ServiceDefaults`
- Python services: `src/AspireApp.PythonServices/app/` (routers, services)
- SQLite: `database/data-resources.db` (shared via bind mount)
- CI: `.github/workflows/squad-ci.yml` (currently placeholder)

**Deliverable:** `plan.md` created with phase-based roadmap, quality gap matrix, and test organization.

**Recommendation to Team:** Start Phase 1 immediately. Cannot merge code safely without CI. Contract tests must precede any refactoring of C# models or Python routes.

**Skill Learned: Cross-Service Contract Testing Pattern**
- C# models require `JsonPropertyName` attributes to match Python field names
- Python Pydantic models must have snake_case fields matching JSON
- Contract tests must verify round-trip serialization (C# → JSON → Python deserialize)
- DateTime must use ISO 8601 format on both sides
- Status/enum casing must be tested explicitly (e.g., "uploaded" lowercase vs "Uploaded")
- Missing field names in JSON should fail test (regression detection)
- Documented in `.squad/skills/` for future contract creation in AspireAI
