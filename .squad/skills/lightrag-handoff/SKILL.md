# LightRAG Handoff Pattern

## When to use

Use this pattern when a local service already extracted document text and LightRAG only needs a clean ingest handoff.

## Pattern

1. Export a stable markdown artifact from the upstream processor.
2. Stage that markdown into the shared LightRAG `INPUT_DIR`.
3. Trigger an explicit `POST /documents/scan` request.
4. Treat scan failures as handoff failures, not as proof of ingestion.

## Why this pattern exists in AspireAI

AspireAI mounts `./data` into both the Python service and the LightRAG container, but that shared directory alone does not cause ingestion. LightRAG's server flow requires an explicit ingest action even when files are already present in the input directory.

## Implementation notes

- Prefer markdown for the staged artifact because it preserves document structure and is easy to inspect.
- Keep staged filenames deterministic (`{document_id}-{sanitized-name}.md`) so retries overwrite cleanly.
- Do not make the canonical Docling/SQLite success path depend on LightRAG availability unless the product explicitly requires it.
