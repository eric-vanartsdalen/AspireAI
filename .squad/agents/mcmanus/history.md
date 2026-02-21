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

**From Keaton:**
- Python routers fundamentally broken: ~10 DatabaseService methods don't exist
- Status casing mismatch ("Uploaded" vs "uploaded") is P0 priority
- ApiService is vestigial, simplify by removing or repurposing

**From Fenster:**
- LightRAG and Ollama missing health checks — blocks webfrontend indefinitely
- Config key mismatch (AI-Chat-Model vs AI-Model) prevents model propagation
- SemanticKernel version skew (1.71.0 vs 1.68.0-alpha)

**From Hockney:**
- Zero automated tests — processing pipeline changes are high-risk
- Python dependencies unpinned — reproducibility issue
- Global exception handler returns raw messages (info leak)
