# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

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
