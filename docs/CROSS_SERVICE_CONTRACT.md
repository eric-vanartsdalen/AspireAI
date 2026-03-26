# Cross-Service Contract: C# ↔ Python

> Canonical reference for how the .NET Web frontend and Python processing service share state.
>
> **Last reviewed:** 2026-03-26

---

## Shared Database (SQLite)

Both services access the same SQLite file (`data-resources.db`) via volume mount. C# uses EF Core; Python uses raw SQL with a connection pool (WAL mode).

### `files` Table — Source of Truth

| Column | Type | Writer | Reader | Notes |
|--------|------|--------|--------|-------|
| `id` | INTEGER PK | C# (auto) | Both | Auto-increment |
| `file_name` | TEXT | C# | Python | Timestamped unique name (e.g., `doc_20240101_123456_ab12.pdf`) |
| `original_file_name` | TEXT | C# | Both | User's original filename |
| `file_path` | TEXT | C# | — | **Host-side directory. Do not use in containers.** |
| `file_hash` | TEXT | C# | — | SHA256 hash |
| `file_size` | INTEGER | C# | Python | Bytes |
| `mime_type` | TEXT | C# | Python | MIME type string |
| `uploaded_at` | DATETIME | C# | Python | UTC timestamp |
| `status` | TEXT | Both | Both | See Status Lifecycle below |
| `processing_started_at` | DATETIME | Python | Both | Set when processing begins |
| `processing_completed_at` | DATETIME | Python | Both | Set on success or error |
| `processing_error` | TEXT | Python | Both | Error message if failed |
| `docling_document_path` | TEXT | Python | — | Container-internal path |
| `total_pages` | INTEGER | Python | Both | Page count after processing |
| `neo4j_document_node_id` | TEXT | Python | — | Neo4j element ID |
| `source_type` | TEXT | C# | — | Always `'upload'` for now |
| `source_url` | TEXT | — | — | Reserved for future web scraping |

### `document_pages` Table

| Column | Type | Writer | Reader | Notes |
|--------|------|--------|--------|-------|
| `id` | INTEGER PK | Python | Both | Auto-increment |
| `file_id` | INTEGER FK | Python | Both | References `files.id` |
| `page_number` | INTEGER | Python | Both | 1-based page number |
| `content` | TEXT | Python | Both | Extracted text |
| `page_metadata` | TEXT | Python | Both | JSON string |
| `neo4j_page_node_id` | TEXT | Python | — | Neo4j element ID |

**Constraint:** `UNIQUE(file_id, page_number)`

---

## Status Lifecycle

```
uploaded → processing → processed
                     → error
```

| Status | Set By | Meaning |
|--------|--------|---------|
| `uploaded` | C# Web | File saved to disk and DB record created |
| `processing` | Python | Docling conversion in progress |
| `processed` | Python | Successfully extracted pages |
| `error` | Python | Processing failed; see `processing_error` |

**Critical:** All status values are **lowercase**. C# must write `"uploaded"`, not `"Uploaded"`.

---

## Processing Trigger Contract

- The Web upload flow stops after saving the file and creating the `files` row with `status='uploaded'`; it does **not** call the Python processing endpoints.
- Python processing starts only when a caller hits `POST /processing/process-document/{id}` or `POST /processing/process-all`. Those endpoints enqueue FastAPI `BackgroundTasks`; there is no automatic polling loop or filesystem watcher.
- Work discovery is SQLite-driven: Python selects rows from `files` whose normalized status is `uploaded` or `error`.
- LightRAG handoff happens inside the processing task after Docling exports markdown. Python stages the markdown into `INPUT_DIR` and then explicitly calls `POST /documents/scan`.
- Files copied straight into the shared data directory without a corresponding `files` row and processing call are ignored.

---

## Physical File Path Resolution

### The Problem

C# runs on the host machine (via `AddProject<>`). Python runs in a Docker container. The `file_path` column stores the host-side directory path (e.g., `C:\Users\...\data` on Windows). This path is meaningless inside the Linux container.

### The Rule

**Python never trusts `file_path` literally.** The processing router calls `DatabaseService.resolve_upload_path(document)` which:

1. Extracts `file_path` (directory) and `filename` (timestamped name) from the document.
2. Combines them into a candidate path.
3. Detects Windows-style paths (e.g., `C:\...\data\uploads`) and extracts the relative portion after the `data` directory segment.
4. Searches runtime data roots (`ASPIRE_DATA_PATH` env var, `/app/data`, repo-relative `data/`, `cwd/data/`) for a matching file.
5. Returns the first candidate that exists on disk.

The resolved path is then passed as a second argument to `DoclingService.process_document(document, resolved_file_path)`.

Example:
- DB `file_path`: `C:\Users\dev\repos\AspireAI\data\uploads`
- DB `file_name`: `report_20240101_143022_a1b2c3d4.pdf`
- Container `ASPIRE_DATA_PATH`: `/app/data`
- Relative extraction: `uploads/report_20240101_143022_a1b2c3d4.pdf`
- Resolved path: `/app/data/uploads/report_20240101_143022_a1b2c3d4.pdf`

### Volume Mounts

| Host Path (relative to repo root) | Container Path | Service |
|------------------------------------|----------------|---------|
| `./data` | `/app/data` | Python, LightRAG |
| `./database` | `/app/database` | Python (fallback DB location) |

C# accesses `./data` directly on the host filesystem via its configured data directory.

---

## Python API Surface (Live)

### Health & Info
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/` | Service info |
| GET | `/health` | Health check (Aspire) |
| GET | `/processing/service-info` | Docling capabilities |

### Document Lifecycle
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/documents/` | List all documents |
| GET | `/documents/unprocessed` | List files awaiting processing |
| GET | `/documents/{id}` | Get single document |
| GET | `/documents/{id}/status` | Get processing status detail |
| GET | `/documents/health/database` | SQLite health check |

### Processing
| Method | Path | Purpose |
|--------|------|---------|
| POST | `/processing/process-document/{id}` | Process single document |
| POST | `/processing/process-all` | Process all unprocessed |
| GET | `/processing/status/{id}` | Get processing status (mirrors `/documents/{id}/status`) |

### RAG / Retrieval
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/rag/search-documents?query=&limit=` | Text search |
| GET | `/rag/document-context/{id}` | Full document context |
| GET | `/rag/page-content/{id}/{page}` | Single page content |
| GET | `/rag/surrounding-pages/{id}/{page}` | Context window |
| POST | `/rag/semantic-search` | Semantic search (body: SemanticQuery) |
| GET | `/rag/health` | RAG services health |

---

## Pydantic Models (Python Side)

These are the wire-format models returned by the live API. C# consumers should match these shapes.

```
Document { id, filename, original_filename, file_path, file_size?, mime_type?, upload_date, processed, processing_status }
DocumentPage { id?, file_id, page_number, content, page_metadata?, neo4j_node_id? }
ProcessingStatus { document_id, status, total_pages?, processed_pages?, error_message?, started_at?, completed_at? }
SemanticQuery { query, document_ids?, limit=10, similarity_threshold=0.7 }
PageContent { page_number, content, metadata? }
```

`ProcessedDocument` still exists as an internal Python processing result, but it is not a persisted SQLite table and not part of the supported cross-service database footprint.

---

## Change Coordination Rules

1. **Schema changes** require updating both C# entities (`DocumentEntities.cs`) and Python schema init (`database_service.py`). Neither service runs migrations — both use `CREATE TABLE IF NOT EXISTS`.
2. **Status value changes** must be reflected in both `FileUploadController.cs` (C#) and `database_service.py` (Python status maps).
3. **New columns** should use `DEFAULT` values to maintain backward compatibility during rolling updates.
4. **Breaking model changes** follow the phased deprecation pattern in `.github/instructions/cross-service-contracts.instructions.md`.
