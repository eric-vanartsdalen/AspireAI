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
