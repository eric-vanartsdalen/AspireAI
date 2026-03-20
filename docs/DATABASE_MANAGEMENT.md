# AspireAI Python endpoint and database contract

This document describes the Python-side contract that remains after the upload-path cleanup. It is the source of truth for how uploaded files are located, how SQLite rows are interpreted, and which FastAPI endpoints are part of the supported upload -> process -> retrieve lifecycle.

## Supported lifecycle

1. The Blazor upload flow writes a row to `files`.
2. Python resolves the physical file from `file_path` (directory) plus `file_name` (stored timestamped name).
3. Docling or the fallback processor reads that resolved file and writes extracted pages to `document_pages`.
4. Neo4j stores document and page relationships for retrieval.
5. Retrieval endpoints query Neo4j and page content that came from that processing pass.

## Upload path contract

The SQLite `files` row is intentionally split:

- `file_path`: the directory that originally held the upload
- `file_name`: the timestamped stored filename
- `original_file_name`: the user-facing filename

Python must not assume `file_path` is already a complete file path.

### Resolution rules

- Join `file_path` and `file_name` to produce the physical file path.
- If the stored path is already a full file path, Python accepts it.
- If the stored path is a Windows host path such as `C:\repo\AspireAI\data`, Python remaps it to the runtime data mount (normally `/app/data`) before reading the file.
- Relative paths under `data`, `uploads`, or `processed` are resolved against the runtime data roots.
- If the file cannot be found after those guardrails, processing fails with an explicit file-not-found error and the row ends in `error`.

## Retained FastAPI surface

### Documents

| Method | Path | Purpose |
|---|---|---|
| GET | `/documents/` | List known uploaded documents |
| GET | `/documents/unprocessed` | List rows still eligible for processing |
| GET | `/documents/{document_id}` | Return one document record |
| GET | `/documents/{document_id}/status` | Return processing status for one document |
| GET | `/documents/health/database` | Lightweight SQLite health check |

### Processing

| Method | Path | Purpose |
|---|---|---|
| POST | `/processing/process-document/{document_id}` | Queue processing for one uploaded document |
| POST | `/processing/process-all` | Queue processing for all rows with upload status |
| GET | `/processing/status/{document_id}` | Return processing status for one document |
| GET | `/processing/service-info` | Report whether full Docling or the fallback processor is active |

### Retrieval

| Method | Path | Purpose |
|---|---|---|
| GET | `/rag/search-documents` | Search processed content |
| GET | `/rag/document-context/{document_id}` | Return full context for one processed document |
| GET | `/rag/page-content/{document_id}/{page_number}` | Return one processed page |
| GET | `/rag/surrounding-pages/{document_id}/{page_number}` | Return nearby pages for citation context |
| POST | `/rag/semantic-search` | Search with optional document filtering |
| GET | `/rag/health` | Check SQLite + Neo4j retrieval dependencies |

Removed admin, schema-sync, and performance-monitoring endpoints are no longer part of the supported API contract.

## SQLite contract

### `files`

`files` is the single source of truth for the upload and processing lifecycle.

Key columns used by Python:

| Column | Meaning |
|---|---|
| `id` | Primary key shared across upload, processing, and retrieval |
| `file_name` | Stored timestamped filename |
| `original_file_name` | Original user-supplied filename |
| `file_path` | Directory that contains the stored file |
| `file_hash` | Duplicate-detection hash |
| `file_size` | Original file size in bytes |
| `mime_type` | Uploaded content type |
| `uploaded_at` | Upload timestamp |
| `status` | `uploaded`, `processing`, `processed`, or `error` |
| `processing_started_at` | Timestamp set when processing begins |
| `processing_completed_at` | Timestamp set when processing ends |
| `processing_error` | Error detail when processing fails |
| `docling_document_path` | Path to the persisted processed document JSON |
| `total_pages` | Number of extracted pages |
| `neo4j_document_node_id` | Neo4j node identifier for the processed document |
| `source_type` | Currently `upload` for filesystem-backed documents; URL rows may exist but do not satisfy the upload-file processing path |
| `source_url` | Optional source URL for non-file rows |

### `document_pages`

`document_pages` stores the extracted page content that backs retrieval.

| Column | Meaning |
|---|---|
| `id` | Primary key |
| `file_id` | Foreign key to `files.id` |
| `page_number` | 1-based page number |
| `content` | Extracted text |
| `page_metadata` | JSON metadata emitted by the processor |
| `neo4j_page_node_id` | Optional Neo4j page node identifier |

### Status lifecycle

`uploaded` -> `processing` -> `processed`

If processing fails at any step, the row transitions to `error` and `processing_error` is populated.

## Notes for maintainers

- Keep `file_path` as a directory contract unless both the Web uploader and Python resolver are updated together.
- Prefer file-table operations over reintroducing legacy sync endpoints or compatibility tables.
- If the runtime mount changes from `/app/data`, update the Python path resolver and this document together.
