# Python Footprint Minimization & Upload Path Normalization — Bob — 2026-02-27

## Context

Review of `roadmap/Tasks.md` P0 items: "Upload Path Normalization" and "Python Footprint Minimization." Full audit of the Python service surface (endpoints, DatabaseService methods, DoclingService path logic) against the C# upload contract.

---

## BLOCKING: Upload Path Normalization

### Problem

`DoclingService.process_document()` line 32 constructs the file path as:

```python
file_path = self.uploads_path / document.file_path
```

This is broken in two ways:

1. **Wrong base**: `self.uploads_path` = `/app/data/uploads`, but C# writes files to the `file_path` directory (typically `/app/data` on host or a Windows path). There is no `uploads` subdirectory in the contract.

2. **Wrong field**: `document.file_path` is the **directory** (e.g., `C:\Users\ericv\...\data`), not the filename. The actual filename is in `document.filename` (`file_name` column).

Combined result: Python tries to open something like `/app/data/uploads/C:\Users\ericv\...\data` — guaranteed `FileNotFoundError`.

### Decision: Container-Relative Path Resolution

Python must **not** use `file_path` from the database for physical file access. The `file_path` column stores the host-side directory, which is meaningless inside a Linux container.

**Rule:** Physical file path = `{container_data_mount}/{file_name}`

- Container data mount is always `/app/data` (from AppHost bind mount)
- `file_name` column has the timestamped unique filename

**Implementation (Jarvis to execute):**

```python
# DoclingService.process_document() — REPLACE line 32
data_mount = Path(os.environ.get("DATA_PATH", "/app/data"))
file_path = data_mount / document.filename

if not file_path.exists():
    raise FileNotFoundError(f"Document file not found: {file_path}")
```

Remove `self.uploads_path` from DoclingService entirely. The `uploads_path` concept is a C# internal detail.

**Coordination:** AppHost should pass `DATA_PATH=/app/data` as an environment variable to the Python container (may already be implicit from the bind mount, but explicit is better).

### Impact

Unblocks Gate B1. Without this fix, no document can ever be processed. One-file change in `docling_service.py`.

---

## Python Endpoint Surface — What Stays vs. Goes

### REMOVE (7 endpoints)

These endpoints are over-engineering for the current state. They either query no-op methods, duplicate other endpoints, or add monitoring complexity we don't need yet.

| Endpoint | Router | Reason |
|----------|--------|--------|
| `GET /documents/health/concurrent-access` | documents | Queries 3 DB methods for connection pool stats. We have 2 concurrent readers. Overkill. |
| `GET /documents/health/schema-sync` | documents | Always returns "healthy" — schema is unified. Dead endpoint. |
| `POST /documents/admin/force-sync` | documents | No-op. Returns "no sync needed." Dead endpoint. |
| `GET /documents/stats/performance` | documents | Aggregates 3 calls for dashboard stats nobody consumes. |
| `GET /documents/health/database` | documents | Redundant with `GET /health` which already checks DB. |
| `GET /processing/status/{document_id}` | processing | Exact duplicate of `GET /documents/{document_id}/status`. |
| `GET /processing/processed-documents` | processing | Reimplements `GET /documents/status/completed`. |

### KEEP (13 endpoints)

| Endpoint | Purpose |
|----------|---------|
| `GET /` | Service info (Aspire) |
| `GET /health` | Health check (Aspire) |
| `GET /documents/` | List all documents |
| `GET /documents/unprocessed` | List unprocessed (processing trigger) |
| `GET /documents/status/{status}` | Filter by status |
| `GET /documents/{document_id}` | Get single document |
| `GET /documents/{document_id}/status` | Processing status |
| `POST /processing/process-document/{id}` | Process single |
| `POST /processing/process-all` | Process all unprocessed |
| `GET /processing/service-info` | Docling capabilities |
| `GET /rag/search-documents` | Text search |
| `GET /rag/document-context/{id}` | Full document context |
| `GET /rag/page-content/{id}/{page}` | Single page content |
| `GET /rag/surrounding-pages/{id}/{page}` | Context window |
| `POST /rag/semantic-search` | Semantic search |
| `GET /rag/health` | RAG service health |

---

## DatabaseService — Methods to Remove

With the endpoint removals, these methods become dead code:

| Method | Used Only By |
|--------|-------------|
| `get_statistics()` | concurrent-access, performance endpoints (removed) |
| `get_active_services()` | concurrent-access endpoint (removed) |
| `get_file_document_sync_status()` | schema-sync, performance endpoints (removed) |
| `force_sync_files_and_documents()` | force-sync endpoint (removed) |
| `save_document()` | Nobody. C# handles inserts via EF Core. Dead code. |

### Methods to KEEP

**Core pipeline (8):**
- `get_file_by_id()`, `get_all_files()`, `get_unprocessed_files()`
- `update_file_status()`, `update_file_processing_results()`
- `save_document_page()`, `get_document_pages()`, `get_page_by_number()`

**Legacy compatibility layer (7):**
- `get_document()`, `get_all_documents()`, `get_unprocessed_documents()`
- `get_documents_by_status()`, `get_processed_document()`, `save_processed_document()`
- `update_processing_status()`

**Infra (3):**
- `health_check()`, `_ensure_database_schema()`, `_row_to_file_dict()`

**Verdict on the compatibility layer:** It stays for now. The routers use Document/ProcessedDocument Pydantic models throughout, and rewriting them to use raw dicts would be a P2 cleanup, not a P0 fix. The wrappers are thin and correct.

---

## Contract Documentation

A `docs/CROSS_SERVICE_CONTRACT.md` should be created documenting:

1. **Shared DB schema** (files table, document_pages table) — canonical column names, types, constraints
2. **Status lifecycle** — `uploaded` → `processing` → `processed` | `error`
3. **Path resolution rule** — `file_path` is host-side; Python uses `DATA_PATH + file_name`
4. **Volume mounts** — host `./data` → `/app/data` in both containers
5. **Retained API surface** — the 16 endpoints listed above
6. **Who writes what** — C# owns upload/insert, Python owns processing/status updates

---

## Execution Plan

| Task | Owner | Effort | Blocks |
|------|-------|--------|--------|
| Fix DoclingService path resolution | Jarvis | 30 min | Gate B1 |
| Remove 7 dead endpoints | Jarvis | 1 hr | Gate G |
| Remove 5 dead DatabaseService methods | Jarvis | 30 min | Gate G |
| Create CROSS_SERVICE_CONTRACT.md | Bob or Jeff | 1 hr | Gate G docs |
| Add DATA_PATH env var to AppHost | Jeff | 15 min | Path normalization |
