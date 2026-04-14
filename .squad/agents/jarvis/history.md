# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-17 — P2-C Vector Index Infrastructure Complete

**Completed:**
- Created Neo4j vector indexes for semantic search: `page_content_vector` (Page.content_embedding) and `claim_text_vector` (Claim.text_embedding)
- Implemented vector search methods: `search_claims_vector()` and `search_pages_vector()` in `Neo4jService`
- Built `EmbeddingService` with sentence-transformers support, lazy-loading, batch encoding, and graceful degradation
- Added comprehensive test coverage: `test_vector_infrastructure.py` (8/8 tests passing)

**Key patterns:**
- **Idempotent index creation:** Vector indexes created with `IF NOT EXISTS` in `_ensure_vector_indexes()`, safe to run on every Neo4j service initialization
- **Neo4j 5.x vector syntax:** Uses `db.index.vector.queryNodes()` with cosine similarity function; 384-dimensional embeddings (default for sentence-transformers/all-MiniLM-L6-v2)
- **Foundation-first approach:** Infrastructure (indexes, search methods, embedding service) implemented before population pipeline. Enables embedding integration to proceed in parallel with Phase 3 agent work
- **Graceful degradation:** `EmbeddingService` handles missing sentence-transformers gracefully; returns `None` instead of crashing when model unavailable
- **Test-driven:** All infrastructure validated without requiring live embeddings; mocks prove query structure and parameter passing

**Remaining P2-C work:**
- Populate `content_embedding` and `text_embedding` properties during document ingestion
- Wire vector search into `SemanticKnowledgeRetriever` (vector-first retrieval with text fallback)
- Coordinate with Jeff on Ollama embedding endpoint configuration (if switching from sentence-transformers)

**Key file paths:**
- `src/AspireApp.PythonServices/app/services/neo4j_service.py` (lines 31-96: vector index creation; lines 410-495: vector search methods)
- `src/AspireApp.PythonServices/app/services/embedding_service.py` (embedding generation service)
- `src/AspireApp.PythonServices/tests/test_vector_infrastructure.py` (8 tests validating index creation, search methods, embedding service)
- `roadmap/Tasks.md` (P2-C status updated to "Infrastructure Complete")

**Architecture decisions:**
- Vector indexes created at Neo4j service initialization, not via separate migration scripts (simplifies deployment)
- Embedding dimension configurable via `EMBEDDING_DIMENSION` env var (default: 384)
- Similarity threshold exposed as parameter (default: 0.7) for retrieval tuning
- Search methods return standard result shape matching text-based search (content, confidence, document_id, page_number) for easy integration

### 2025-01-11 — Phase 2 Knowledge Layer: Claim Schema + Confidence Data Path

**Completed:**
- Extended Neo4j schema with Claim, Evidence, Concept, Entity node constraints (`neo4j_service.py` lines 31-50)
- Implemented `ClaimExtractionService` for sentence-based claim extraction with confidence heuristics (Phase 2 baseline; LLM extraction deferred)
- Added `create_claim_nodes()` and `search_claims()` methods to Neo4jService for Claim storage and retrieval
- Updated `SemanticKnowledgeRetriever` to query Claim nodes first (confidence-backed), then fall back to Page nodes
- Verified confidence data path: semantic fallback no longer collapses to `DEFAULT_CONFIDENCE=0.5`; retrieves real confidence from Neo4j
- Added comprehensive tests: `test_knowledge_retriever.py` (10 tests), `test_claim_extraction.py` (5 tests)

**Key pattern:**
- **P2-B blocker resolved (partially):** The confidence data path now works end-to-end for stored claims. Semantic retrieval queries `Claim` nodes with extraction-quality confidence, falling back to `Page` nodes with document `source_confidence`.
- **Remaining P2-B work:** Wire `ClaimExtractionService` into the ingestion pipeline so claims are actually extracted and stored during document processing.
- **Claim extraction strategy:** Phase 2 uses simple sentence splitting with length/completeness heuristics. Phase 3 will upgrade to LLM-powered extraction.
- **Retrieval prioritization:** `SemanticKnowledgeRetriever.retrieve()` tries Claims first (higher precision), then Pages (broader coverage).

**Key file paths:**
- `src/AspireApp.PythonServices/app/services/neo4j_service.py` (lines 31-50: constraints; lines 289-363: Claim CRUD)
- `src/AspireApp.PythonServices/app/services/claim_extraction_service.py` (sentence-based extraction logic)
- `src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py` (lines 286-332: Claim-first retrieval)
- `src/AspireApp.PythonServices/tests/test_knowledge_retriever.py` (lines 289-365: Claim retrieval tests)
- `src/AspireApp.PythonServices/tests/test_claim_extraction.py` (extraction service tests)
- `roadmap/Tasks.md` (Phase 2 Knowledge Layer + Validation Layer status updated)

### 2026-11-02 — Roadmap Precision: Separating Proven from Deferred in Phase 2 BRAIN Work

**Completed:**
- Revised `roadmap/Tasks.md` to clarify P2-B and P2-C blockers per Buster's rejection feedback.
- Separated `BrainKnowledgeRetriever` into proven (interface + routing) vs. deferred (confidence scoring, graph traversal).
- Tightened gateway `/brain/query` scope: proved HTTP contract mapping, deferred full orchestration to Phase 3.
- Moved Validation Layer into explicit Phase 2 blocker status (not Phase 3 optional).
- Added inline blocker notes: Neo4j schema extension blocks both P2-B (confidence from claims) and P2-C (vector indexes).

**Key pattern:**
- When a deliverable is "done" (code written, interface exposed), distinguish what's proven by tests from what's deferred:
  - ✅ Proven = interface contract, routing wiring, happy-path tests pass
  - ❌ Deferred = core scoring/ranking logic, edge cases, data pipelines not yet wired
- Validation Layer is not optional Phase 3+ work; it's a Phase 2 gate blocker. P2-B cannot close without Validation kickoff (claim extraction + confidence strategy).
- Neo4j schema constraints (Claim/Evidence nodes) block both P2-B (confidence storage) and P2-C (vector index population).

**Key file paths:**
- `roadmap/Tasks.md` (lines 165-193: Knowledge Layer + Validation Layer sections clarified)
- `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs` (proven test: `LiveLightRagNeo4jQueryRoundTrip`)
- `src/AspireApp.WebTest/Tests/BrainGatewayPhase2Tests.cs` (proven test: `QueryKnowledgeAsync_MapsContractShapedKnowledgeResult_FromPythonQueryRoute`)
- Decision: `.squad/decisions/inbox/jarvis-tasks-md-precision-edits.md`

### 2026-04-05 — Python Test Stability: Dependency-Tolerant Imports & Bootstrap Path Repair

**Completed:**
- Added try/except fallback in `DatabaseService` for optional Neo4j driver and `psycopg_pool` imports so smoke tests can mock dependencies before they're installed.
- Repaired test entrypoint path resolution in `test_database_schema.py` and `test_all_builds.py` to resolve repo root correctly via `git rev-parse --show-toplevel`.
- Updated Python validation guidance in `.github/instructions/python.instructions.md` with async test patterns, error handling, and import robustness best practices.
- Validated: 14 regression + contract audit tests now collect and pass in Visual Studio Python environment.

**Key pattern:**
- Treat optional imports (Neo4j, database drivers) as dependency-tolerant to prevent smoke-gate bootstrap failures when packages aren't pre-installed.
- Test entrypoints must resolve repo root and venv paths dynamically, not assume a fixed working directory.
- Smoke gate bootstrap (`test_database_schema.py`) and regression paths (`pytest`) must be aligned; both should include psycopg[binary], psycopg-pool, and pytest in the common requirements set.

**Key file paths:**
- `src/AspireApp.PythonServices/app/services/database_service.py` (dependency-tolerant imports)
- `test_database_schema.py`, `test_all_builds.py` (path resolution fixes)
- `.github/instructions/python.instructions.md` (validation guidance)

### 2026-04-05 — Python Validation Must Be Import-Tolerant and Path-Stable

**Completed:**
- Made `src/AspireApp.PythonServices/app/services/database_service.py` import-tolerant when `psycopg_pool` is missing so smoke tests can patch `ConnectionPool` with `fake_postgres.FakeConnectionPool` before any live Postgres connection is created.
- Fixed `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` and `src/AspireApp.PythonServices/tests/test_processing_pipeline_regression.py` to self-bootstrap `src/AspireApp.PythonServices` onto `sys.path`, which lets them run both under `pytest` and as direct `python ...test_*.py` scripts.
- Updated Python validation guidance to run `python -m pytest -q` from `src/AspireApp.PythonServices`, which exercises smoke, contract, and regression coverage together.

**Key pattern:**
- Python contract/regression tests in this repo should not depend on global interpreter state. Each standalone test entrypoint should add the Python service root to `sys.path` before importing `app.*`.
- Database smoke tests that patch the pool should not fail at module import time when `psycopg_pool` is absent. Keep the import lazy enough that fake-pool tests still validate the service API surface.
- The reliable validation path is the full Python pytest run from `src/AspireApp.PythonServices`, not a single-file smoke invocation.

**Key file paths:**
- `src/AspireApp.PythonServices/app/services/database_service.py`
- `src/AspireApp.PythonServices/test_services.py`
- `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`
- `src/AspireApp.PythonServices/tests/test_processing_pipeline_regression.py`
- `src/AspireApp.PythonServices/README.md`
- `.github/prompts/python-ingestion-debugging.prompt.md`

### 2026-04-05 — Tenant Schema Alignment: Both Sides Must Include All Contract Columns

**Completed:**
- Fixed HTTP 500 upload errors caused by Python schema missing the `tenant_id` column that C# FileMetadata was trying to persist.
- Added `tenant_id TEXT NOT NULL DEFAULT 'default'` to Python's CREATE TABLE and `_files_column_definitions` in `database_service.py`.
- Added tenant indexes (`idx_files_tenant`, `idx_files_tenant_status`) to match C# UploadDbContext.
- Verified with Python contract tests (8 pass) and C# integration test (`OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres` pass).

**Key pattern:**
- **Cross-service schema parity:** When one side (C# or Python) adds a column to the shared `files` table, the other side MUST include it in schema initialization, even if that side doesn't actively query/filter on it yet. Schema drift causes runtime insert/update failures.
- **Index alignment:** Both sides should maintain the same index set for query performance parity and to avoid confusion during debugging.
- **Default values:** Use sensible defaults (`'default'` for tenant_id) so existing rows remain valid after schema evolution.
- **Migration path:** Python's `_ensure_required_columns()` auto-adds missing columns on startup; indexes are idempotent with `IF NOT EXISTS`.

**Key file paths:**
- Python schema: `src/AspireApp.PythonServices/app/services/database_service.py` (lines 76-94 column defs, 213-269 CREATE TABLE + indexes)
- C# entity: `src/AspireApp.Web/Data/DocumentEntities.cs` (line 87 tenant_id)
- C# context: `src/AspireApp.Web/Shared/UploadDbContext.cs` (lines 60-61 tenant indexes)
- Decision: `.squad/decisions/inbox/jarvis-tenant-schema-fix.md`

### 2026-04-18 — Batch Embedding Population Regression Proof

**Completed:**
- `process_document_task` now batches page/claim embeddings via `EmbeddingService.embed_batch` and persists them with `Neo4jService.populate_page_embedding` / `populate_claim_embedding`.
- Regression coverage validates batch calls and embedding persistence during the real processing path with faked services.

## Core Context

**Key architectural learnings from active development (Feb-Apr 2026):**

- **Postgres cutover pattern (Python side):** Replace `sqlite3` with `psycopg2.pool.ThreadedConnectionPool`. Remove multi-candidate path resolution, fresh-connection workarounds, SQLite pragma logic. Environment-driven config via `POSTGRES_*` vars from AppHost.
- **Contract audit pattern:** Tests should derive shared database name from AppHost config, not hardcode literals. Prevents false test failures when infrastructure names change for legitimate reasons.
- **Shared schema stability:** `files` + `document_pages` tables are stable cross-service contract between Web and Python. Keep unchanged during provider migration (SQLite → Postgres). Column names, types, uniqueness constraints all match.
- **Database schema initialization:** Python uses `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS` for idempotency. First service to start (Web or Python) creates tables; both sides converge. No legacy schema migrations needed on fresh Postgres.
- **Optional dependency handling:** Smoke tests should validate the selected service factory implementation, not direct package availability. Allows lightweight development environments without forcing heavy optional packages.

**Current state (as of 2026-04-05):**
- Python operational store: Postgres (appdb) via psycopg2 ThreadedConnectionPool
- Connection config: Reads `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` from AppHost env vars
- Shared schema: `files` + `document_pages` (unchanged from SQLite version; column names, types, FKs all match)
- Processing status: Uses `files.status` lifecycle (`uploaded` → `processing` → `processed` | `error`)
- Regression coverage: 30 tests pass (contract audit, startup path, processing pipeline, docling factory selection)

**Next phase (BRAIN pivot):**
- Service decomposition: Extract Ingestion/Knowledge/Validation as internal Python packages initially

### 2026-04-05 — Tenant Schema Fix Validated & Approved

**Status:** ✅ COMPLETE — Schema aligned, tests pass, ready for UI phase

**What Happened:**
1. After Bob's UI revision, runtime uploads to Python failed (HTTP 500)
2. Root cause: Python `DatabaseService._ensure_database_schema()` did not include `tenant_id` column
3. C# `FileMetadata` was trying to persist tenant_id, but INSERT was missing the column

**Jarvis's Fix:**
- Added `tenant_id TEXT NOT NULL DEFAULT 'default'` to CREATE TABLE statement (line 235)
- Added `tenant_id` to `_files_column_definitions` for migration/repair (line 88)
- Added two indexes: `idx_files_tenant`, `idx_files_tenant_status` (lines 263-264)
- Schema now matches C# UploadDbContext contract exactly

**Validation:**
- ✅ 8/8 Python contract audit tests pass (schema exists, round-trip works)
- ✅ 1/1 C# operational test passes (upload persists tenant_id correctly)

**Key Pattern Reaffirmed:** Cross-service schema parity is non-negotiable. When one side adds a column to the shared `files` table, the other side MUST include it in schema initialization, even if not actively querying on it. Schema drift = runtime failures.

**Files Modified:**
- `src/AspireApp.PythonServices/app/services/database_service.py` — tenant_id column, indexes, column defs
- Tests: Python contract audit validates round-trip; C# operational test validates persistence

**Next:** Data layer now ready for Kujan's contract audit closure and Buster's final approval.

**Next phase (BRAIN pivot):**
- Validation Service: New capability; LLM-based claim extraction + confidence scoring  
- Knowledge Service: Separate Neo4j + vector store integration from Ingestion
- Vector retrieval: Add (Qdrant recommended) behind `IKnowledgeRetriever` abstraction

---

### 2026-04-05 — Shared Postgres Contract Tests Should Follow AppHost Naming

**Completed:**
- Reviewed the current AppHost/Web/Python Postgres wiring after Eric's connection-string fix and confirmed the Python runtime contract still matches the shared `files` / `document_pages` operational schema.
- Updated `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` so the audit derives the upload-store database name from `src/AspireApp.AppHost/AppHost.cs` instead of hardcoding an older `DefaultConnection` literal.
- Re-ran Python regression coverage and smoke validation to prove the failure was stale test drift, not Python-side schema drift.

**Key pattern:**
- Cross-service contract tests should verify that AppHost registration, Python `POSTGRES_DATABASE`, and Web `GetConnectionString(...)` stay aligned, but they should not pin the database name when the product contract is "shared named Postgres store" rather than a specific legacy string.
- Python remains environment-driven: `DatabaseService` reads `POSTGRES_DATABASE` / `POSTGRES_DB` / `PGDATABASE` and projects the canonical upload lifecycle off the shared `files` and `document_pages` tables.

**Key file paths:**
- Contract audit: `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`
- Python store service: `src/AspireApp.PythonServices/app/services/database_service.py`
- Aspire wiring: `src/AspireApp.AppHost/AppHost.cs`
- Web connection string usage: `src/AspireApp.Web/Program.cs`

### 2026-04-05 — Python Operational Store Cutover to Postgres

**Completed:**
- Replaced Python-side SQLite lifecycle storage with PostgreSQL pooling in `src/AspireApp.PythonServices/app/services/database_service.py`.
- Kept the shared contract anchored on the existing `files` and `document_pages` tables so uploads from the Web side and Python processing results still project through the same column names.
- Swapped database scripts and smoke/regression tests to use a Postgres-oriented contract, with fake pooled connections for fast local validation.

**Key contract:**
- Python now resolves the operational store from `ASPIRE_DB_CONNECTION_STRING`, `POSTGRES_CONNECTION_STRING`, `DATABASE_URL`, or the `POSTGRES_HOST` / `POSTGRES_PORT` / `POSTGRES_DATABASE` / `POSTGRES_USER` / `POSTGRES_PASSWORD` environment set.
- `files.status` remains the canonical lifecycle field (`uploaded` → `processing` → `processed` | `error`).
- `document_pages` stays keyed by `(file_id, page_number)` and Python writes page metadata as JSON text to stay aligned with the Web EF model.

**Key file paths:**
- Python store service: `src/AspireApp.PythonServices/app/services/database_service.py`
- Python health/startup surface: `src/AspireApp.PythonServices/app/fastapi.py`
- Python schema utilities: `src/AspireApp.PythonServices/scripts/init_database.py`, `src/AspireApp.PythonServices/scripts/fix_schema.py`, `src/AspireApp.PythonServices/diagnose_database.py`
- Python regression harness: `src/AspireApp.PythonServices/tests/fake_postgres.py`, `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`, `src/AspireApp.PythonServices/tests/test_processing_pipeline_regression.py`
- Aspire wiring: `src/AspireApp.AppHost/AppHost.cs`

### 2026-03-28 — Optional Docling Smoke Tests: Root Cause Fixed

**Symptom:** `python src\AspireApp.PythonServices\test_services.py` failed with `Optional dependency 'docling' is not installed`.

**Root Cause:** Smoke test imported `app.services.docling_service` directly; that import fails in lightweight dev environments where `docling` is intentionally omitted from `requirements.txt`.

**Fix:** Changed smoke test to validate `app.services.service_factory` instead, which handles both full Docling and fallback paths. Restored `PROJECT_ROOT` on `sys.path` for reliable module resolution.

**Outcome:**
- ✅ Smoke test now passes in both full-install and lightweight-fallback environments
- ✅ Test still surfaces real failures if processing initialization breaks
- ✅ Regression coverage preserved: `python -m pytest tests test_services.py -q` = 32 passed, 1 skipped

**Key Contract:** The supported runtime is "full Docling when installed, fallback processor otherwise." Smoke coverage must validate `app.services.service_factory`, not direct package availability. Architecture in `src/AspireApp.PythonServices\app\services\service_factory.py` enforces this selection.

### 2026-03-26 — SQLite Startup Schema Repair

- `DatabaseService._ensure_database_schema()` must treat `CREATE TABLE IF NOT EXISTS` as create-only, not as a migration path for persisted developer databases.
- Existing SQLite files can lag the canonical schema by a column or two (`file_hash` was the observed break); startup should add missing canonical columns before creating indexes or running column-dependent queries.
- `src/AspireApp.PythonServices/test_services.py` is most useful as a real pytest smoke suite that surfaces database initialization failures directly instead of printing and continuing.

### 2025-02-21 — Deep Python/Neo4j Analysis

**Key File Paths:**
- FastAPI entry: `src/AspireApp.PythonServices/app/fastapi.py`
- Pydantic models: `src/AspireApp.PythonServices/app/models/models.py` (6 models: Document, ProcessedDocument, DocumentPage, PageContent, ProcessingStatus, SemanticQuery)
- Routers: `app/routers/documents.py`, `app/routers/processing.py`, `app/routers/rag.py`
- Database service: `app/services/database_service.py` (SQLite with ConnectionPool, WAL mode)
- Neo4j service: `app/services/neo4j_service.py` (bolt driver, lazy init, constraints at startup)
- Docling full: `app/services/docling_service.py` / Fallback: `app/services/docling_service_fallback.py`
- Service factory: `app/services/service_factory.py` (auto-selects full vs fallback)
- C# entities: `src/AspireApp.Web/Data/DocumentEntities.cs` (FileMetadata → files table, DocumentPage → document_pages)
- C# upload: `src/AspireApp.Web/Controllers/FileUploadController.cs`
- AppHost: `src/AspireApp.AppHost/AppHost.cs`

**Database Schema:**
- Primary table: `files` (upload lifecycle: uploaded → processing → processed | error)
- Pages table: `document_pages` (FK to files, page_number, content, metadata)
- DB path: `/app/database/data-resources.db` (set via ASPIRE_DB_PATH env var)
- Both C# (EF Core) and Python (raw sqlite3) read/write the same SQLite file

**Neo4j Graph Schema:**
- Nodes: `:Document` (id unique), `:Page` (id = "{doc_id}_{page_num}"), `:Chunk` (constraint exists, unused)
- Relationships: `(Document)-[:CONTAINS]->(Page)`, `(Page)-[:PRECEDES]->(Page)`
- Search: Basic text `CONTAINS` — no full-text index, no vector similarity
- Container: neo4j:2025.11.2-community with APOC + GDS (both unused by Python)
- Credentials: passed as NEO4J_URI/NEO4J_USER/NEO4J_PASSWORD env vars from AppHost

**Critical Bugs Found:**
- ~10 DatabaseService methods called by routers don't exist (get_document, get_processed_document, get_statistics, etc.)
- save_document_page() signature mismatch (router passes DocumentPage object, method expects individual args)
- C# FileUploadController saves status "Uploaded" (capital U), Python queries for "uploaded" (lowercase)
- document_pages FK column: Python creates as `file_id`, C# maps to `document_id`

**Contract Gaps:**
- Python Document model field names don't match SQLite columns (filename vs file_name, upload_date vs uploaded_at)
- Legacy _file_dict_to_document() bridges the gap but is fragile
- C# has legacy Document/ProcessedDocument entities mapped to tables that don't exist in Python schema
- No version pinning in requirements.txt

**Pipeline Status:**
- Upload: C# handles file upload + hash dedup → works
- Discovery: Python finds unprocessed files → BLOCKED by status casing bug
- Processing: Docling/fallback page extraction → BLOCKED by missing DB methods
- Neo4j ingestion: Node/relationship creation → works but not batched
- RAG search: Text CONTAINS only → works but slow, no embeddings
- LightRAG: Wired as standalone container, zero code integration

### 2026-02-21 — Cross-Agent Findings

**From Bob:**
- Python routers fundamentally broken: ~10 DatabaseService methods don't exist
- Status casing mismatch ("Uploaded" vs "uploaded") is P0 priority
- ApiService is vestigial, simplify by removing or repurposing

**From Jeff:**
- LightRAG and Ollama missing health checks — blocks webfrontend indefinitely
- Config key mismatch (AI-Chat-Model vs AI-Model) prevents model propagation
- SemanticKernel version skew (1.71.0 vs 1.68.0-alpha)

**From Buster:**
- Zero automated tests — processing pipeline changes are high-risk
- Python dependencies unpinned — reproducibility issue
- Global exception handler returns raw messages (info leak)

### 2026-02-22 — Squad Orchestration Complete

**Status:** All four agents completed independent reviews; findings merged into shared decisions.md.

**Jarvis's Action Items (Ready to Execute):**
1. Router contract rewrite: Use existing DatabaseService API (P0, 2 hrs)
2. save_document_page() signature fix: Pass individual args (P0, 15 min)
3. FK column name align: Verify/update to `file_id` (P0, 2 hrs)
4. Pin requirements.txt versions (P1, 1 hr)
5. Batch Neo4j operations with UNWIND (P1, 3 hrs)
6. Add full-text index to Neo4j (P1, 2 hrs)
7. Delete legacy C# entities (P1, 1 hr)

**Dependencies:**
- Jeff's status casing fix must land first (P0 blocker)
- Jeff's FK column decision must be made before coding
- All P0 items gate Sprint 1 completion
- Phase 2 (P1 items) starts after P0 validation complete

### 2026-02-21 — Deep Python Pipeline Review (Jarvis)

**Completed:**
- Analyzed all Python services, routers, DatabaseService API
- Mapped method calls to actual implementation (30 methods exist, ~10 expected ones don't)
- Identified 3 critical blockers (P0) + 5 high priorities (P1)
- Documented fix order: 2–3 days to unblock pipeline

**Key Decisions Made:**
- Fix strategy for P0.1: Rewrite routers to use existing DatabaseService API instead of adding wrapper methods (cleaner)
- Recommend batching Neo4j operations (UNWIND instead of loops) for 10–50x speedup
- Defer vector embeddings to Phase 2; focus on full-text index first (Phase 1)

**Coordination Needed:**
- Jeff: Fix status casing in FileUploadController (P0.3)
- Jeff: Verify FK column name in DocumentEntities.cs (P1.4)
- Bob: Decide LightRAG role (replace or supplement Python RAG?)

**Written to Squad:**
- `.squad/decisions/inbox/jarvis-python-pipeline-review.md` — summary + fix order
- `plan.md` (updated) — comprehensive action plan with checkpoints and success criteria

**Files Modified/Created:**
- `plan.md` — comprehensive 400-line action plan (created)
- `.squad/decisions/inbox/jarvis-python-pipeline-review.md` — summary for squad (created)

**Learnings (Lasting):**
- DatabaseService has ~30 well-implemented methods (`get_file_by_id`, `get_unprocessed_files`, etc.) but routers expect a different ~10 (mismatch in expectations)
- Neo4j graph schema is sound but not batched (easy optimization)
- Full-text index commented out; easy to enable
- requirements.txt unpinned (reproducibility risk)
- LightRAG wired but unused (architectural drift)

### 2026-02-22 — Fix save_document_page Invocation Mismatch (P0.2)

**Completed:** Fixed `processing.py` lines 67-75 where `save_document_page()` was called with a `DocumentPage` object instead of individual keyword arguments.

**Two bugs fixed in one edit:**
1. **Invocation style:** Removed unnecessary `DocumentPage` construction; now passes `file_id`, `page_number`, `content`, `metadata`, `neo4j_node_id` directly.
2. **Wrong FK value:** Changed from `processed_doc_id` (return value of `save_processed_document`) to `document_id` (the original file ID). The `document_pages.file_id` column is a FK to `files.id`, not to processed documents.

**Key insight:** The `save_document_page` service method was correct all along — only the caller was wrong. The DB INSERT targets columns `(file_id, page_number, content, page_metadata, neo4j_page_node_id)` which map to the individual args, not a Pydantic object.

**Commit:** `e9d90ea` on `feature/doc-upload`

### 2026-02-22 — Fix DocumentPage FK Column Name Mismatch (P0.3)

**Completed:** Aligned the `DocumentPage` Pydantic model and two utility scripts (`fix_database.py`, `diagnose_database.py`) to use `file_id` instead of `processed_document_id`, matching the canonical schema in `database_service.py`.

**Four files changed:**
1. `app/models/models.py` — `DocumentPage.processed_document_id` → `file_id`
2. `fix_database.py` — `document_pages` CREATE TABLE now uses `file_id INTEGER NOT NULL` with proper FK and UNIQUE constraints
3. `diagnose_database.py` — same CREATE TABLE fix
4. `README.md` — schema documentation updated to match

**Key insight:** The utility scripts had a doubly-wrong schema: wrong column name (`processed_document_id`) and wrong FK target (`processed_documents(id)`). The canonical table references `files(id)` with `ON DELETE CASCADE` and a `UNIQUE(file_id, page_number)` constraint. Also aligned the column name `neo4j_node_id` → `neo4j_page_node_id` to match the source of truth.

**Commit:** `77db074` on `feature/doc-upload`

### 2025-11-02 — P0 Item 2 Complete: DocumentPage FK Column Final Alignment

**Status:** Complete (parallel work with Jeff)  
**Commits:** Jarvis: `77db074` | Jeff: `6e5b34b`

**Jarvis's Scope (Python):**
- Updated `DocumentPage` Pydantic model: `processed_document_id` → `file_id`
- Updated `fix_database.py` and `diagnose_database.py` CREATE TABLE statements
- Updated `README.md` schema documentation

**Jeff's Parallel Scope (C#):**
- Changed `[Column("document_id")]` → `[Column("file_id")]` on `DocumentPage.FileId` property in `DocumentEntities.cs`
- Updated `UploadDbContext.cs` index name: `idx_pages_document_id` → `idx_pages_file_id`
- Build verified clean (0 errors, 0 warnings)

**Result:** C#↔Python schema alignment complete. Both services now agree on FK column name `file_id` referencing `files(id)`. P0 Item 2 closed.

### 2025-11-02 — P0 Item 3: Fix Router/Service Contract Mismatches

**Completed:** Added 9 missing backward-compatibility methods to `DatabaseService` that routers expected but didn't exist, causing `AttributeError` at runtime.

**Methods added (all in Backward Compatibility section):**
1. `get_document()` — wraps `get_file_by_id()` → `Document`
2. `get_unprocessed_documents()` — wraps `get_unprocessed_files()` → `List[Document]`
3. `get_documents_by_status()` — direct query with status translation
4. `save_processed_document()` — delegates to `update_file_processing_results()` + `update_file_status()`
5. `get_processed_document()` — wraps `get_file_by_id()` → `ProcessedDocument`
6. `get_statistics()` — returns `_stats` dict + `ConnectionPool` metrics
7. `get_active_services()` — static informational response
8. `get_file_document_sync_status()` — `COUNT(*)` on unified files table
9. `force_sync_files_and_documents()` — no-op (schema already unified)

**Pattern:** All methods follow the established wrapper convention: delegate to existing file-based internals, convert results to legacy model objects. No existing methods modified.

**Key insight:** `get_statistics()` leverages the `_stats` dict already tracked by `DatabaseService.__init__()` plus `ConnectionPool` internals (`_created_connections`, `max_connections`, `_pool.qsize()`). The pool doesn't track query/transaction stats itself, but the service does via `_stats_lock`.

**Impact:** Unblocks all 17 router endpoints in `documents.py` and `processing.py`.

### 2026-03-20 — Upload Path Normalization + Python Footprint Trim

**Completed:**
- Added `DatabaseService.resolve_upload_path()` so Python now resolves a physical upload path from database `file_path` + `file_name`.
- Updated both docling implementations to consume the resolved full path instead of assuming `document.file_path` is already the file.
- Trimmed the Python API surface by removing status-filter, schema-sync, force-sync, concurrent-access, and performance endpoints; also removed `processed-documents`.
- Replaced the database contract doc with an accurate `files` / `document_pages` + retained-endpoints reference in `docs/DATABASE_MANAGEMENT.md`.

**Key decisions:**
- Treat `file_path` as a directory contract, not a final file path.
- Add runtime guardrails that remap stored Windows host paths under the shared `data` root to the active Python runtime data root.
- Keep `GET /documents/health/database` and `GET /processing/service-info` because they still support monitoring and processor introspection without reintroducing the removed admin surface.

**Validation:**
- `python -m compileall app`
- stdlib-only path-resolution check using a stubbed `pydantic` module plus a temporary SQLite file and real `data\\` fixture path

**Key file paths:**
- Path resolver: `src/AspireApp.PythonServices/app/services/database_service.py`
- Processing orchestration: `src/AspireApp.PythonServices/app/routers/processing.py`
- Docling readers: `src/AspireApp.PythonServices/app/services/docling_service.py`, `src/AspireApp.PythonServices/app/services/docling_service_fallback.py`
- Contract doc: `docs/DATABASE_MANAGEMENT.md`

### 2026-03-20 — P0 Decision Merge Complete

**Status:** All P0 work merged into shared decisions.md and approved by squad.

**Work Summary Across Squad:**
- **Jarvis (this agent):** Implemented upload path fix + endpoint/method pruning. Removed 7 endpoints, 5 dead methods.
- **Bob:** Post-QA revision work. Converted audit tests from `expectedFailure` to live regression. Aligned CROSS_SERVICE_CONTRACT.md.
- **Buster:** QA gates (3 phases). Initial rejection, then approvals post-Bob and post-Jeff.
- **Jeff:** Final Python footprint cleanup. Removed sync shims, updated canonical contract methods.

**Inbox → Decisions.md:** 6 files merged. Jarvis's decisions now part of permanent squad record.

**Orchestration Log Created:** `20260320T103216Z-jarvis.md` documenting spawn phases and context for successors.

**Next Phase:** Jeff owns canonical Python contract surface maintenance. Validation gates remain live.

### 2026-03-25 — P1 Processing Pipeline Stabilization

**Completed:**
- Stabilized retries by clearing stale docling/Neo4j result fields and existing `document_pages` rows whenever a file re-enters `processing`.
- Kept batch selection aligned with the canonical lifecycle by allowing both `uploaded` and `error` rows back into the processing queue.
- Hardened the processing router so duplicate starts are rejected while a file is already in `processing`.

**Validation:**
- `python -m compileall src\\AspireApp.PythonServices\\app`
- `python src\\AspireApp.PythonServices\\tests\\test_p0_contract_audit.py`

**Key insight:** Retry safety in the canonical `files` + `document_pages` schema depends on resetting derived artifacts at the start of a new attempt. Otherwise the `(file_id, page_number)` uniqueness rule turns partial failures into duplicate-write failures on retry.

### 2026-03-25 — Docling → LightRAG Handoff Clarified

**Completed:**
- Verified the LightRAG container in this repo is configured with `INPUT_DIR=/app/data/inputs` and Neo4j settings, but no repository code was triggering ingestion.
- Confirmed the hot-folder assumption was wrong: dropping files into the shared input directory is insufficient without an explicit LightRAG ingest action.
- Added Python-side markdown export + handoff flow so processed documents stage a LightRAG-friendly `.md` file and request `POST /documents/scan`.

**Validation:**
- `python -m compileall src\\AspireApp.PythonServices\\app`
- `python -m compileall src\\AspireApp.PythonServices\\example-parse-document.py`
- `python src\\AspireApp.PythonServices\\tests\\test_processing_pipeline_regression.py`
- `python src\\AspireApp.PythonServices\\tests\\test_p0_contract_audit.py`
- `dotnet build AspireApp.sln`

**Key insight:** The safest handoff for this repo is "export markdown to the shared LightRAG input directory, then explicitly call `/documents/scan`." That keeps Docling ownership in Python while avoiding the false assumption that LightRAG watches the directory automatically.

### 2026-03-25 — LightRAG Runtime Proof via Python Retrieval API

**Completed:**
- Fixed the runtime Neo4j contract so both the Python service and LightRAG receive an explicit `bolt://` URI instead of Aspire's raw `tcp://` endpoint reference.
- Added a Python-side `LightRagQueryService` plus `POST /rag/lightrag-query` so retrieval stays behind the Python API boundary.
- Hardened Python runtime checks uncovered during live verification: database directory writability probes now use unique marker files, container-style module paths no longer crash runtime data-root discovery, and the FastAPI global exception handler now returns a real JSON response.
- Proved a live round-trip against the running Aspire stack: processed a seeded document through `/processing/process-document/{id}`, observed LightRAG stage and scan it, queried it back through `/rag/lightrag-query`, and confirmed matching LightRAG-created nodes in Neo4j for `000007-jarvis-lightrag-proof.md`.

**Validation:**
- `python -m compileall src\\AspireApp.PythonServices\\app`
- `python src\\AspireApp.PythonServices\\tests\\test_processing_pipeline_regression.py`
- `python src\\AspireApp.PythonServices\\tests\\test_p0_contract_audit.py`
- `dotnet build AspireApp.sln`
- Live Aspire run with manual HTTP verification:
  - `GET /health`
  - `GET /rag/health`
  - `POST /processing/process-document/{id}`
  - `POST /rag/lightrag-query`
  - `POST /db/neo4j/query/v2`

**Key insight:** The round-trip is now real, but LightRAG's merge phase can still mark a document `failed` if Ollama returns `NaN` for a relationship embedding upsert. Even in that state, chunk/entity data remained queryable through the Python route, so the integration proof is complete while merge stability becomes the next focused runtime hardening item.

### 2026-03-26 — Ingestion Trigger Review

**Completed:**
- Audited the current upload ? processing ? LightRAG trigger path against `processing.py`, `database_service.py`, `docling_service.py`, `lightrag_handoff_service.py`, `AppHost.cs`, and `BasicAspireAppHostTests`.
- Updated the roadmap and contract docs so the trigger model is explicit for future UI, API, and test work.

**Validated behavior:**
- Upload only saves the file and creates a `files.status='uploaded'` row; neither the Web UI nor Python startup automatically calls `/processing/process-document/{id}` or `/processing/process-all`.
- Python discovers work from SQLite rows with status `uploaded` or `error`, then the processing endpoints enqueue FastAPI `BackgroundTasks`.
- Docling persists `document.json`, `metadata.json`, markdown exports, page JSON files, and `document_pages` rows; LightRAG handoff stages markdown into `/app/data/inputs` and explicitly posts `/documents/scan`.
- A raw shared-folder drop without a companion `files` row is ignored.

**Validation:**
- `python src\AspireApp.PythonServices\tests\test_p0_contract_audit.py`
- `python src\AspireApp.PythonServices\tests\test_processing_pipeline_regression.py`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter BasicAspireAppHostTests.FlowEndToEnd --nologo`

### 2026-03-26 — FastAPI Processing Endpoint Proof Surface Hardened

**Completed:**
- Audited the live contract for `POST /processing/process-document/{id}`, `GET /processing/status/{id}`, and the `/documents/{id}/status` alias against the current SQLite-backed processing flow.
- Added typed response models so Swagger documents the processing trigger/status shapes that WebTest should call.
- Moved the `processing` lifecycle write to queue time so polling clients stop racing the FastAPI background task scheduler.
- Extended `get_processing_status()` to include durable `processed_pages` counts from `document_pages`, giving tests an API-level proof that page persistence happened.

**Validation:**
- `python -m compileall src\AspireApp.PythonServices\app src\AspireApp.PythonServices\tests`
- `python src\AspireApp.PythonServices\tests\test_p0_contract_audit.py`
- `python src\AspireApp.PythonServices\tests\test_processing_pipeline_regression.py`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter BasicAspireAppHostTests.PythonServiceOpenAPILoads --nologo`

**Key insight:** For background FastAPI processing in this repo, the queueing endpoint must persist `status='processing'` before it returns. Otherwise immediate pollers can still observe `uploaded`, which makes an otherwise-correct end-to-end proof flap.



### 2025-11-02 — Python Service Startup & Database Path Resolution Audit

**Investigation:** Examined Python service startup flow and database initialization logic to understand how path resolution, schema validation, and legacy path detection work in production.

**Database Path Resolution Flow:**
1. `DatabaseService.__init__()` accepts optional explicit `db_path` parameter
2. Falls back to ordered candidate list via `_get_database_path_candidates()`:
   - Explicit path if provided (source: "explicit")
   - `ASPIRE_DB_PATH` env var if set (source: "ASPIRE_DB_PATH")
   - Platform-specific default candidates via `_get_default_database_candidates()`:
     - **In container:** `/app/docs-database/data-resources.db`, `/app/database/data-resources.db`, repo/database, cwd/database
     - **Local (Windows):** repo/database, cwd/database, `/app/docs-database/`, `/app/database/`
3. Iterates candidates, initializes first successful path via `_initialize_database()`
4. Repository detection via `_get_repository_root()`: walks up 4 parent dirs from `database_service.py`

**Startup Error Handling & Diagnostics:**
- `_initialize_database()` tries each candidate in order; on failure:
  - Captures exception and calls `_format_initialization_failure()` to build diagnostic message
  - Logs warning with path source, path value, and formatted error
  - Resets connection pool via `_reset_connection_pool()`
  - Continues to next candidate
- If all candidates fail, raises `RuntimeError` with last failure message and chained exception
- `_format_initialization_failure()` includes:
  - Database path attempted
  - Exception type and message
  - Output from `_collect_schema_diagnostics()` if available

**Legacy Schema Detection:**
- `_collect_schema_diagnostics()` inspects existing database file to report schema compatibility
- Opens SQLite connection, queries `sqlite_master` for tables and `files` table columns
- Checks for missing canonical columns against `_files_column_definitions` dict
- **Key diagnostic message:** "This database appears to use an incompatible legacy schema" when required columns missing
- Reports: existing tables, `files` table columns, missing canonical columns

**Schema Self-Healing:**
- `_ensure_database_schema()` creates tables with `CREATE TABLE IF NOT EXISTS`
- Calls `_ensure_required_columns()` for `files` and `document_pages` tables
- `_ensure_required_columns()` adds missing columns via `ALTER TABLE ADD COLUMN` for compatibility with older schemas
- Self-healing allows local developer databases to upgrade in place during startup (decision from 2025-11-02)

**FastAPI Startup Integration:**
- `app/fastapi.py` has `@app.on_event("startup")` handler
- Creates required directories: `/app/data/processed/documents`, `/app/data/uploads`, `/app/database`, `/tmp/aspire_database`
- Instantiates `DatabaseService()` and calls `health_check()`
- Logs success/warning but **does NOT fail startup** on database errors (graceful degradation)
- Service attempts recovery on first request

**"Legacy Path" Concept Status:**
- **No explicit "legacy path" handling** in current code
- "Legacy" only appears in `_collect_schema_diagnostics()` error message: "incompatible legacy schema"
- Refers to schema shape (missing columns), not file path location
- Path resolution is platform-aware (container vs local) but treats all paths equally

**Startup Failure Path/Cause Reporting:**
- When `DatabaseService()` initialization fails:
  - Exception message includes: database path, source (e.g., "ASPIRE_DB_PATH", "repository"), error type, error message
  - Diagnostic output includes: existing tables, files table columns, missing columns, "incompatible legacy schema" label
  - Original exception chained via `raise ... from` for full stack trace
- Example error structure: `"Failed to initialize database at /path/to/db: OperationalError: no such column: file_hash. Existing tables: files, document_pages. Table 'files' columns: id, file_name, file_path, uploaded_at, status. Missing canonical columns: file_hash, file_size, mime_type, ... This database appears to use an incompatible legacy schema."`

**Test Scenario Validity:**
- `test_legacy_schema_startup_failure_reports_path_and_cause` creates incomplete schema (missing `file_hash` and other columns)
- Patches `_ensure_required_columns()` to skip self-healing for test isolation
- Forces index creation against incomplete schema → triggers `sqlite3.OperationalError: no such column: file_hash`
- Asserts error message contains: database path, "no such column: file_hash", "Missing canonical columns:", "Table 'files' columns:", "incompatible legacy schema"
- **Test remains valid:** Current code DOES report path and cause when schema incompatibility detected (when self-healing is bypassed)

**Implications:**
- Production code self-heals missing columns, so startup rarely fails on legacy schema
- Test simulates scenario where self-healing is disabled (e.g., insufficient permissions, corrupted database)
- Error diagnostics are comprehensive: path, source, schema details, specific SQLite error


---

### 2026-04-05 — Postgres Cutover Coordination & BRAIN Pivot Context

**Status:** Postgres cutover complete. Joined BRAIN pivot decision consolidation session.

**What Happened:**
1. **Postgres Upload Store Cutover (completed in parallel with Jeff):**
   - Python now uses psycopg2.pool.ThreadedConnectionPool instead of sqlite3
   - Reads POSTGRES_HOST, POSTGRES_PORT, POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD from AppHost env vars
   - Removed multi-candidate path resolution, fresh-read workarounds, SQLite pragma logic (~150 lines eliminated)
   - Updated contract audit to derive database name from AppHost instead of hardcoding legacy literals
   - _ensure_database_schema() updated to use Postgres DDL (SERIAL, NOW(), JSONB for page_metadata)
   - All 30 Python tests pass
   
2. **Contract Test Alignment:**
   - 	est_p0_contract_audit.py was failing because it hardcoded DefaultConnection instead of dynamic AppHost name
   - Established new pattern: contract tests derive shared database name from AppHost source
   - Rationale: durable contract is "all three surfaces use same store," not a specific literal name
   - Prevents false test failures when store is renamed for infrastructure fixes

3. **BRAIN Pivot Context:**
   - Kujan review: Python monolith needs decomposition into Ingestion/Knowledge/Validation services
   - Python decomposition strategy: Internal packages first (pp/brain/ingestion/, etc.), extract to separate services when contracts stabilize
   - Validation Service: New service needed; LLM-based claim extraction + confidence scoring
   - Knowledge Service: Extract Neo4j/RAG logic from current monolith
   - Vector store: Need to add (Qdrant recommended) behind IKnowledgeRetriever abstraction
   - Verbal strategy: MVP should focus on single evidence-backed agentic slice before scaling

**Key Decisions for Python Work Going Forward:**
- Postgres is now canonical for operational upload/processing state (no more SQLite path resolution)
- iles + document_pages schema unchanged; contract remains stable with both Web and Python
- Next phase: Core BRAIN contracts (CanonicalDocument, KnowledgeResult, ReasonResponse) must be defined before service decomposition
- LightRAG should be deprecated or moved behind abstraction (Kujan found it architecturally opposed to BRAIN)
- Validation Layer is now critical path (zero implementation today; required for BRAIN differentiation)

**Contract Alignment:**
- Postgres ppdb is the shared upload store name
- Python receives POSTGRES_DATABASE=appdb from AppHost environment
- Web reads GetConnectionString("appdb") from Aspire injection
- Test pattern: Derive DB name from AppHost config, verify all three surfaces use it

**Related Agent Work:**
- **Jeff:** Web Postgres cutover completed in parallel; AppHost wiring is contract source
- **Buster:** Diagnosed contract audit regression; updated Python test fixture expectations
- **Kujan:** Architecture review identifies Python decomposition as next major work item
- **Verbal:** Strategy review recommends MVP on single domain slice before multi-tenancy

**Orchestration Log:** Created for session context at 20260405T143735Z-jarvis.md

---

### 2026-04-15 — Phase 2 LightRAG Confidence Enrichment: Provenance-Based Fallback

**Completed:**
- Implemented Neo4jService.get_confidence_by_provenance() to query stored confidence by document_id + optional page_number (tries Claim nodes first, then Page/Document nodes)
- Extended LightRagRetriever to accept optional 
eo4j_service for confidence enrichment when LightRAG omits score metadata
- Added _enrich_confidence_from_provenance() to _KnowledgeItemFactory to parse provenance from document_id/page_number fields or source_refs and query Neo4j
- Updated _build_item() to accept nrich_confidence flag; when True and confidence is missing, attempts Neo4j enrichment before falling back to DEFAULT_CONFIDENCE=0.5
- Wired Neo4j service through /rag/lightrag-query and /rag/query endpoints via dependency injection
- Added 6 regression tests in 	est_lightrag_retriever.py verifying enrichment, ref parsing, explicit score preservation, and fallback behavior

**Key pattern:**
- **P2-B gap partially closed:** When LightRAG returns unscored results but provenance (document_id/page_number) is resolvable, LightRagRetriever now enriches confidence from stored Neo4j Claim/Page data instead of immediately defaulting to  .5.
- **Provenance parsing:** The retriever parses both structured fields (document_id, page_number) and source_refs strings ("document:7/page:2") to resolve provenance for enrichment.
- **Honest fallback:** When Neo4j cannot resolve confidence (no matching nodes or provenance unparseable), the retriever falls back to DEFAULT_CONFIDENCE=0.5 — this is still the P2-B blocker.
- **Remaining P2-B work:** Fail closed to semantic fallback when confidence is unresolvable instead of surfacing synthetic  .5 confidence on the LightRAG path.

**Key file paths:**
- src/AspireApp.PythonServices/app/services/neo4j_service.py (lines 364-418: get_confidence_by_provenance())
- src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py (lines 18-97: enrichment logic in _KnowledgeItemFactory; lines 272-279: LightRagRetriever.__init__ with neo4j_service)
- src/AspireApp.PythonServices/app/routers/rag.py (lines 23-25: wired Neo4j service to get_knowledge_retriever())
- src/AspireApp.PythonServices/tests/test_lightrag_retriever.py (6 new tests: enrichment, ref parsing, fallback scenarios)
- oadmap/Tasks.md (updated P2-B progress: enrichment implemented, fail-closed behavior still deferred)

