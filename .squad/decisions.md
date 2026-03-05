# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.

<!-- Decisions are appended below. Each entry starts with ### -->

## Architecture Review — Bob (Lead/Architect) — 2026-02-21

**Scope:** Comprehensive architecture review of AspireAI solution

### CRITICAL: Python Router↔DatabaseService Contract Misalignment

Python Pydantic models and routers are out of sync with the DatabaseService class. The routers call ~10 methods (`get_document()`, `get_unprocessed_documents()`, `get_processed_document()`, `save_processed_document()`, etc.) that **do not exist** on the current DatabaseService. This causes `AttributeError` at runtime on most document and processing endpoints.

**Decision:** Either add the missing wrapper methods to DatabaseService or rewrite routers to call the current API (`get_file_by_id()`, `get_unprocessed_files()`, `get_all_files()`). Option (b) is cleaner.

**Impact:** Processing pipeline (Gate B1/B2) is completely blocked until fixed. ~1 day effort.

### HIGH: Status Casing Mismatch

FileUploadController.cs line 123 writes `"Uploaded"` (capital U) to the status field, but Python's `get_unprocessed_files()` queries `WHERE status = 'uploaded'` (lowercase). **Files uploaded via the C# Web UI will never be found by Python for processing.**

**Decision:** Normalize to lowercase `"uploaded"` in FileUploadController.cs. One-line fix unblocks entire pipeline.

**Impact:** Fixes file discovery. ~30 minutes.

### HIGH: ApiService is Vestigial

AspireApp.ApiService contains only a weather forecast stub. The entire project is boilerplate with no integration into the document pipeline. Web frontend communicates directly with Python service and SQLite. ApiService adds startup latency and orchestration complexity for zero value.

**Decision:** Three options: (a) keep as facade/proxy, (b) merge into Web, (c) remove entirely. Recommend (c) now. Add real API gateway later if needed.

**Impact:** Simplifies startup, reduces orchestration bloat. Medium-term effort for removal.

### MEDIUM: SQLite Concurrent Access Risk

Two services (Web + Python) share one SQLite file via bind mount with dual access patterns: EF Core in C# and raw SQL in Python. SQLite with WAL mode handles this reasonably, but there's no coordination on schema migrations. Cold-start race condition possible.

**Decision:** WaitFor(pythonServices) in AppHost mitigates. Add explicit schema version check at startup if needed. Monitor in production.

**Impact:** Low immediate risk given WaitFor ordering. Document as potential issue.

### MEDIUM: LightRAG Integration is Wired But Unverified

LightRAG container is registered with full configuration in AppHost but has no health check or integration test. Web frontend `WaitFor(lightrag)` blocks on it. No Python or C# code references LightRAG APIs.

**Decision:** Either add `.WithHttpHealthCheck()` for LightRAG or remove `WaitFor(lightrag)` from webfrontend until integration code exists. Improves startup time during development.

**Impact:** Fixes startup blocking. ~1 hour.

### MEDIUM: global.json Targets .NET 10 Preview

`global.json` specifies SDK 10.0.0 with `allowPrerelease: true`. All `.csproj` files target `net10.0`. This is fine for learning but creates dependency on preview SDK availability. README still references ".NET 9" (stale).

**Decision:** Document .NET 10 requirement clearly in README. Consider pinning to stable SDK release when available.

**Impact:** Maintenance task. Update docs immediately.

### LOW: Legacy Entity Dead Weight

`DocumentEntities.cs` carries deprecated `Document` and `ProcessedDocument` EF entities mapped to non-existent `documents` and `processed_documents` tables. These legacy tables are created by EF Core alongside the canonical `files` + `document_pages` tables, cluttering the schema.

**Decision:** Remove deprecated entities and migrations after verifying no remaining code references them.

**Impact:** Reduces confusion. ~2 hours effort.

### LOW: No Automated Tests

No test projects exist in the solution. `test_all_builds.py` and `test_database_schema.py` are utility scripts, not CI-ready test suites. Roadmap's "Testing Baseline" task is unstarted.

**Decision:** Establish test infrastructure (CI build + test projects) before closing schema migration. Create C# xUnit project and Python pytest suite per Buster's recommendations.

**Impact:** Enables safe refactoring. Foundation work required.

---

## .NET Deep Analysis — Jeff (.NET Dev) — 2026-02-21

**Scope:** .NET projects deep dive, build health, package dependency alignment

### Config Key Mismatch: AI-Chat-Model vs AI-Model

AppHost.cs line 129 passes `AI-Chat-Model` as environment variable, but Web project's `AiInfoStateService` and `HomeConfigurations` look for `AI-Model`. The model name may not propagate correctly through Aspire-injected env vars.

**Decision:** Align environment variable naming. Update either AppHost to use `AI-Model` or Web services to check `AI-Chat-Model`. Prefer consistent naming across all Aspire services.

**Impact:** Fixes AI model propagation. ~30 minutes.

### LightRAG and Ollama Have No Health Checks

LightRAG is registered with `AddContainer()` but has no `WithHttpHealthCheck()`. Ollama has no explicit health check configured. The webfrontend `WaitFor()` will wait indefinitely if either fails to start properly.

**Decision:** Add `.WithHttpHealthCheck()` for both LightRAG (port 9621) and Ollama (port 11434). If health check endpoints don't exist, remove from WaitFor chain until integration code ready.

**Impact:** Fixes startup blocking, improves debugging. ~1 hour.

### SemanticKernel Version Mismatch

`Microsoft.SemanticKernel` is at 1.71.0 but `Microsoft.SemanticKernel.Connectors.Ollama` is at 1.68.0-alpha. These should be kept in sync to avoid runtime compatibility issues.

**Decision:** Update Connectors.Ollama to match core SK version (1.71.0).

**Impact:** Fixes dependency skew. ~30 minutes.

### Duplicate ServiceDiscoveryUtilities Class

Two classes with the same name exist in different namespaces:
- `AspireApp.Web.ServiceDiscoveryUtilities` (root namespace)
- `AspireApp.Web.Components.Pages.ServiceDiscoveryUtilities` (Pages namespace)

They have different method signatures and behavior. `HomeConfigurations` uses Pages version, `AiInfoStateService` uses root version. Maintenance hazard.

**Decision:** Consolidate into single class in shared namespace. Verify both call sites work correctly after merge.

**Impact:** Reduces confusion. ~1-2 hours.

### OllamaWarmupService Creates Raw HttpClient

Line 88 creates `new HttpClient()` instead of using `IHttpClientFactory` from DI. This bypasses resilience policies and proper lifecycle management.

**Decision:** Inject `IHttpClientFactory` into OllamaWarmupService constructor and use it to create HttpClient.

**Impact:** Follows .NET guidance. ~30 minutes.

### Console.WriteLine Used Extensively

Both `Chat.razor.cs` (35+ instances) and other services use `Console.WriteLine` for debug output instead of `ILogger<T>`. This bypasses structured logging and won't appear in Aspire telemetry.

**Decision:** Replace all `Console.WriteLine` with injected `ILogger<T>`. Scope to high-impact files (Chat, FileUploadController) initially.

**Impact:** Improves observability. Medium-term cleanup.

### ApiService /health Endpoint Only Mapped in Development

`ServiceDefaults.MapDefaultEndpoints()` line 115 has `if (app.Environment.IsDevelopment())`. Health endpoints won't exist in production. AppHost registers `WithHttpHealthCheck("/health")` for apiservice, creating a mismatch.

**Decision:** Either map `/health` unconditionally or adjust AppHost expectations. For now, document as dev-only during Aspire runs.

**Impact:** Fixes health check for production deployments. ~1 hour.

### Redundant IConfiguration Registration

`Program.cs` line 53: `builder.Services.AddSingleton<IConfiguration>(builder.Configuration)` is unnecessary. `IConfiguration` is already registered by the host builder.

**Decision:** Remove redundant registration.

**Impact:** Cleanup. ~5 minutes.

---

## Python Services & Neo4j Deep Analysis — Jarvis (Python/Data) — 2026-02-21

**Scope:** Python service architecture, API endpoints, contract alignment, Neo4j schema validation

### CRITICAL: ~10 Missing DatabaseService Methods

The routers call methods that don't exist on the current `DatabaseService` class:
- `get_document()`, `get_unprocessed_documents()`, `get_documents_by_status()`
- `get_processed_document()`, `save_processed_document()`
- `get_statistics()`, `get_active_services()`, `get_file_document_sync_status()`, `force_sync_files_and_documents()`

These cause `AttributeError` at runtime on most document, processing, and health check endpoints.

**Decision:** Implement missing methods as thin wrappers around the current `get_file_by_id()` / `get_unprocessed_files()` API, or rewrite routers to call existing methods directly. Option (b) is cleaner and aligns with "minimal Python footprint" goal.

**Implemented by Jarvis (2025-11-02):** Added 9 backward-compatibility wrapper methods to `DatabaseService`. Wrapper methods delegate to existing file-based methods + model conversion. Preserves router API contract unchanged; reuses proven internal methods; consistent with existing pattern. Commit: (from inbox decision).

**Impact:** Unblocks processing pipeline. ~1 day effort → complete.

### CRITICAL: save_document_page() Signature Mismatch

`processing.py` line 75 calls `db.save_document_page(page_record)` passing a DocumentPage object, but the actual signature is `save_document_page(self, file_id, page_number, content, metadata, neo4j_node_id)` expecting individual arguments.

**Decision:** Update call to pass individual arguments: `db.save_document_page(file_id, page_number, content, metadata, node_id)`.

**Implemented by Jarvis (2025-11-02):** Fixed method invocation in `processing.py`. Commit `e9d90ea`. P0 Item 2 complete.

**Impact:** Fixes document processing crash. ~1 hour → complete.

### HIGH: Status Casing Mismatch ("Uploaded" vs "uploaded")

C# FileUploadController writes `"Uploaded"` (capital U) but Python queries for `"uploaded"` (lowercase). Files uploaded via Web UI will never be found by Python.

**Decision:** Change C# to write lowercase `"uploaded"` to match Python expectations (also matches other status values: processing, processed, error).

**Implemented by Jeff (2025-11-02):** Normalized FileUploadController.cs line 123 `"Uploaded"` → `"uploaded"`. Commit `62ee545`. P0 Item 4 complete.

**Impact:** Enables file discovery. ~30 minutes → complete.

### HIGH: FK Column Name Mismatch on document_pages

| Side | Column Name |
|------|-------------|
| **Python** (CREATE TABLE) | `file_id` |
| **C#** (EF Core [Column] attribute) | `document_id` |

Whichever service creates the table first determines the actual column name. The other will fail or behave incorrectly.

**Decision:** Decide on canonical name (recommend `file_id` for consistency with foreign key semantics). Update C# [Column] attribute to match Python CREATE TABLE statement. Verify both sides agree before cold-start.

**Implemented by Jeff & Jarvis (2025-11-02):** Aligned to canonical `file_id`. C# [Column] attribute updated; Python schema unchanged. Commits: Jeff `6e5b34b`, Jarvis `77db074`. P0 Item 2 complete.

**Impact:** Fixes data integrity risk. ~2 hours → complete.

### HIGH: Legacy C# Entities Reference Non-Existent Tables

`DocumentEntities.cs` has `Document` mapped to `documents` table and `ProcessedDocument` mapped to `processed_documents` table. Neither table exists in Python schema. This dead code could cause confusion or conflict during migrations.

**Decision:** Remove deprecated entities after verifying no remaining code references them.

**Impact:** Reduces confusion. ~1-2 hours.

### MEDIUM: requirements.txt Has No Version Pinning

All dependencies are unpinned (`fastapi`, `uvicorn`, `neo4j`, `docling-core`, etc.). Builds are non-reproducible. `docling` especially is heavy; upgrades could break processing pipeline.

**Decision:** Pin all dependencies with version constraints. Use `pip freeze` to generate reproducible requirements. Example:
```
fastapi==0.104.1
uvicorn==0.24.0
neo4j==5.14.0
docling-core==1.2.0
```

**Impact:** Enables reproducible builds. ~1 hour.

### MEDIUM: Neo4j Operations Not Batched

Pages and relationships are created one-by-one in loops instead of batched with `UNWIND`. This is slow at scale.

**Decision:** Refactor page and relationship creation to use batch `UNWIND` queries. Example:
```cypher
UNWIND $pages as page
CREATE (p:Page {id: page.id, document_id: page.document_id, ...})
```

**Impact:** Improves processing performance. ~4 hours.

### MEDIUM: No Full-Text or Vector Index

Neo4j search uses string `CONTAINS` (very slow at scale). Vector index is commented out in neo4j.conf. GDS and APOC plugins installed but unused.

**Decision:** Create full-text index for text search. Enable vector index for semantic search once embeddings are added. Example:
```cypher
CREATE FULLTEXT INDEX ft_page_content FOR (p:Page) ON EACH [p.content]
```

**Impact:** Enables scalable search. ~2 days for vector integration.

### LOW: LightRAG Container Has Zero Python Integration

LightRAG is wired in AppHost as separate container with Ollama connection and Neo4j access. **No Python code calls LightRAG APIs.** Web frontend waits for it but doesn't use it. Completely standalone.

**Decision:** Clarify LightRAG role: Is it replacing the custom Python RAG pipeline or supplementing it? Document decision and either (a) wire Python endpoints to call LightRAG, or (b) remove from AppHost/startup until integration code ready.

**Impact:** Clarifies architecture. Depends on product decision.

---

## Quality Audit — Buster (QA) — 2026-02-21

**Scope:** Automated test inventory, CI/CD health, code quality patterns

### CRITICAL: Zero Automated Tests

The solution has no test projects and no automated test suite:
- **C#:** 0 test projects (no xUnit/NUnit/MSTest)
- **Python:** 6 "test" files, but NONE are actual tests. All are manual diagnostic scripts with no assertions, no pytest runner, no conftest.py, no pytest.ini.

The files `test_all_builds.py`, `test_database_schema.py`, `test_services.py`, and `test_concurrent_access.py` are utility/benchmark scripts, not pytest-integrated tests.

**Decision:** Establish test infrastructure before closing schema migration:
1. Create `AspireApp.UnitTests.csproj` (xUnit)
2. Add `pytest` to requirements.txt
3. Create `conftest.py` and `pytest.ini`
4. Create integration test suites per Buster's Phase 2-3 roadmap

**Impact:** Enables safe refactoring of P0/P1 fixes. Blocks PR merge until CI passes.

### CRITICAL: CI/CD Pipeline is Non-Functional

`squad-ci.yml` is placeholder: `echo "No build commands configured"`. No build verification, no tests run, PRs merge unchecked.

**Decision:** Update CI workflow to:
1. Run `dotnet build` (with Aspire stopped to avoid file locks)
2. Run `dotnet test` once test projects exist
3. Run `pytest` on Python services once test suite created
4. Block PR merge until all checks pass

**Impact:** Prevents regression. Foundation work.

### HIGH RISK: Logging Uses Console.WriteLine

7+ files use `Console.WriteLine` instead of `ILogger<T>`:
- `Chat.razor.cs` (35+ instances)
- `Program.cs`, `HomeConfigurations.cs`, `ServiceDiscoveryUtilities.cs`, `AiInfoStateService.cs`, `SpeechService.cs`

This bypasses structured logging and OpenTelemetry integration. Debug output won't appear in Aspire dashboard logs.

**Decision:** Replace with `ILogger<T>`. Inject logger into services/components. Prioritize high-impact files (Chat, Controllers).

**Impact:** Improves observability. Medium-term refactoring.

### HIGH RISK: No Cross-Service Contract Tests

C#↔Python communication has no validation tests. If C# changes field names or types, Python models silently diverge. This is a primary vector for runtime failures.

**Decision:** Create contract test suite that verifies:
1. C# records serialize to JSON matching Python Pydantic model field names
2. Python models deserialize C# JSON correctly
3. Enum values and status strings match
4. DateTime formats are compatible

Run these tests in CI on every build.

**Impact:** Prevents contract drift. ~1-2 days effort.

### MEDIUM: Broad catch(Exception) Everywhere

27+ catch(Exception) blocks across C# and Python swallow errors or re-expose generically. Error context is lost, making debugging hard.

**Decision:** Prioritize specific exception catches:
- Catch `FileNotFoundException`, `InvalidOperationException` individually
- Log with context (document ID, operation)
- Re-throw with context or return structured error response

**Impact:** Improves debuggability. Medium-term refactoring.

### MEDIUM: Python Dependencies Unpinned

`requirements.txt` has no version pins. Builds are non-reproducible. Docling is especially volatile (heavy ML dependencies).

**Decision:** Pin all versions. Use `pip freeze` to generate reproducible requirements.

**Impact:** Enables reproducible builds. ~1 hour.

### Test Coverage Gap Matrix Priority

| Feature | Risk | Recommended Test Type |
|---------|------|----------------------|
| Chat feature | 🔴 HIGH | Unit (service logic) + Integration (E2E with Ollama mock) |
| File Upload | 🔴 HIGH | Unit (validation) + Integration (controller → storage) |
| Processing pipeline | 🔴 HIGH | Integration (Python DatabaseService → Neo4j) |
| Cross-service contracts | 🔴 HIGH | Contract tests (JSON serialization) |
| Python routes | 🔴 HIGH | Unit (TestClient) + mocked dependencies |
| Neo4j queries | Medium | Unit (mocked driver) + integration (real Neo4j) |

---

## Instructions Consolidation — Bob (Lead/Architect) — 2026-02-27

**Scope:** Merge project-specific context with Squad boilerplate into unified root instructions file

### Consolidate copilot-instructions.md

`.github/copilot-instructions.md` was replaced with 47-line Squad boilerplate, losing all project-specific context (architecture overview, day-one setup, troubleshooting, instruction lookup, repo map). Consolidated both versions into a single 167-line file that:

1. **Opens with team personas** — Bob, Jeff, Jarvis, Buster described as domain owners with distinct voices
2. **Restores operational context** — Quick Overview, Day-One Checklist, Build/Run/Test, Validation Before PR, Troubleshooting Cheatsheet, Repo Map
3. **Retains Squad conventions** — team context, capability self-check, branch naming, PR guidelines, decision inbox
4. **Updates all references** — .NET 10 SDK (from global.json), all 15 instruction files, all 12 prompt files

**Principles:** Personas first (set ownership/tone), reference not replicate (keep root file scannable), correct versions (synced with project reality), unified voice.

**Impact:** All squad members read updated file for current conventions. No instruction files modified; only root consolidation.

---

## DocumentPage FK Column Name Alignment — Jeff & Jarvis — 2025-11-02

**Scope:** P0 Item 2 — Resolve FK column name mismatch on `document_pages` table

### RESOLVED: DocumentPage FK Column Alignment

The `document_pages` table had conflicting column names across language boundaries:
- **Python (source of truth):** `file_id` INTEGER NOT NULL (referencing `files(id)`)
- **C# (EF Core):** `[Column("document_id")]` on `FileId` property

This created a data integrity risk: C# and Python would disagree on the actual column name in the database.

**Decision:** Aligned to canonical `file_id` (Python-defined, semantically correct).

**Implementation:**
- **Jeff (C#):** Updated `DocumentEntities.cs` `[Column("document_id")]` → `[Column("file_id")]`. Updated `UploadDbContext.cs` index name. Build verified clean. Commit: 6e5b34b.
- **Jarvis (Python):** Updated `DocumentPage` Pydantic model, `fix_database.py`, `diagnose_database.py`, `README.md` schema docs. Commit: 77db074.

**Impact:** Fixed schema alignment. P0 Item 2 closed. No more C#↔Python column name conflicts on `document_pages`.


