---
name: "processing-retry-reset"
description: "Reset persisted pipeline artifacts before retrying a file-processing attempt"
domain: "python-pipeline"
confidence: "high"
source: "manual"
---

## Context

AspireAI persists both lifecycle state in `files` and derived page content in `document_pages`. If a processing attempt fails after writing some output, the next retry must begin from a clean persisted state.

## Patterns

### Reset Derived Output When Entering `processing`

At the start of a new attempt, clear any fields produced by the previous run:

- `processing_completed_at`
- `processing_error`
- `docling_document_path`
- `total_pages`
- `neo4j_document_node_id`
- all `document_pages` rows for the file

Tie that cleanup to the same transition that sets `status = 'processing'` so retries behave deterministically.

### Explicit URL Refresh Should Reuse Cleanup + Requeue

For URL-backed rows that need a manual refresh, keep the flow explicit:

1. clean up prior external artifacts,
2. reset the persisted row back to `uploaded`,
3. then reuse the normal processing start endpoint.

In AspireAI this lets the Web UI refresh stale web pages or YouTube-backed rows without inventing a second backend refresh contract.

### Treat `error` Rows as Retryable Work

Batch processing should pull both `uploaded` and `error` rows when looking for work. Failed files are still part of the canonical pipeline; they just need a clean new attempt.

### Block Duplicate Active Starts

Reject attempts to start processing when a row is already in `processing`. Retries are for failed work, not concurrent work on the same file.

## Examples

```python
db.update_file_status(file_id, "processing")
```

In AspireAI, that transition now also clears stale processing artifacts and deletes old page rows before Docling runs again.

## Anti-Patterns

- **Retrying without clearing `document_pages`** — this collides with `UNIQUE(file_id, page_number)`.
- **Treating `error` rows as invisible to batch processing** — this forces manual recovery.
- **Implicitly reprocessing already-processed rows** — a new attempt should always be explicit.
