# AspireAI Python Processing Service

FastAPI service for document processing, page extraction, and retrieval support in AspireAI.

## What it owns

- Reads uploaded file metadata from PostgreSQL
- Resolves the physical upload path inside the container
- Processes files with Docling or the fallback processor
- Persists extracted page content to `document_pages`
- Writes retrieval graph data to Neo4j

## Canonical database footprint

The live Python service is built on two PostgreSQL tables:

- `files` — upload + processing lifecycle state
- `document_pages` — extracted page content keyed by `file_id`

Legacy `documents` and `processed_documents` tables are retired and not part of the supported runtime contract.

## Quick start

### Local development

```powershell
Set-Location src\AspireApp.PythonServices
python setup_dev_env.py
.venv\Scripts\activate
uvicorn app.fastapi:app --host 0.0.0.0 --port 8000 --reload
```

### Full Aspire orchestration

```powershell
dotnet run --project src\AspireApp.AppHost
```

## API surface

### Documents

- `GET /documents/` — list uploaded documents
- `GET /documents/unprocessed` — list rows still eligible for processing
- `GET /documents/{document_id}` — get one document
- `GET /documents/{document_id}/status` — get processing status for one document
- `GET /documents/health/database` — lightweight PostgreSQL health check

### Processing

- `POST /processing/process-document/{document_id}` — queue one document for processing
- `POST /processing/process-all` — queue every uploaded document
- `GET /processing/status/{document_id}` — get processing status
- `GET /processing/service-info` — report whether full Docling or fallback processing is active

### Retrieval

- `GET /rag/search-documents?query={query}&limit={limit}` — search extracted content
- `GET /rag/document-context/{document_id}` — retrieve all pages for one document
- `GET /rag/page-content/{document_id}/{page_number}` — retrieve one page
- `GET /rag/surrounding-pages/{document_id}/{page_number}` — retrieve nearby pages
- `POST /rag/semantic-search` — semantic retrieval with optional filters
- `POST /rag/lightrag-query` — contract-shaped LightRAG retrieval returning `KnowledgeResult`
- `GET /rag/health` — check PostgreSQL + Neo4j retrieval dependencies

## Processing flow

1. Blazor writes a `files` row with status `uploaded`.
2. Python resolves the runtime file path from `file_path` + `file_name`.
3. Docling extracts document structure and page text.
4. Python updates the same `files` row with processing results.
5. Python writes page content to `document_pages`.
6. Neo4j receives document/page nodes for retrieval features.

## PostgreSQL schema

### `files`

| Column | Purpose |
|---|---|
| `id` | Shared primary key for upload, processing, and retrieval |
| `file_name` | Stored timestamped filename |
| `original_file_name` | User-visible filename |
| `file_path` | Stored directory path |
| `file_hash` | Duplicate-detection hash |
| `file_size` | File size in bytes |
| `mime_type` | Uploaded content type |
| `uploaded_at` | Upload timestamp |
| `status` | `uploaded`, `processing`, `processed`, or `error` |
| `processing_started_at` | Set when processing begins |
| `processing_completed_at` | Set when processing finishes |
| `processing_error` | Error detail when processing fails |
| `docling_document_path` | Persisted processed document JSON path |
| `total_pages` | Extracted page count |
| `neo4j_document_node_id` | Neo4j document node reference |
| `source_type` | `upload` for the current path |
| `source_url` | Reserved for future non-file sources |

### `document_pages`

| Column | Purpose |
|---|---|
| `id` | Primary key |
| `file_id` | Foreign key to `files.id` |
| `page_number` | 1-based page number |
| `content` | Extracted text |
| `page_metadata` | JSON metadata from the processor |
| `neo4j_page_node_id` | Neo4j page node reference |

## Upload path rules

- `file_path` is treated as a directory, not a guaranteed full file path.
- `file_name` is joined with `file_path` to form the runtime candidate path.
- Windows host paths are remapped to the container data mount when needed.
- If the file cannot be resolved, Python marks the row as `error`.

## Useful helpers

```powershell
python diagnose_database.py
python fix_database.py
python scripts\fix_schema.py --check-only
python scripts\test_concurrent_access.py --threads 5 --operations 20
python ..\..\test_database_schema.py
```

## Validation

```powershell
Set-Location src\AspireApp.PythonServices
python -m pytest -q
```
