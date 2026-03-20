---
name: "canonical-file-contract-projection"
description: "Project API document/status models from a canonical file table without keeping legacy sync wrappers"
domain: "python-contracts"
confidence: "high"
source: "manual"
---

## Context

AspireAI's Python service kept legacy document/processed-document helper methods after the runtime schema had already consolidated onto `files` and `document_pages`. That left routers, scripts, and docs speaking in two footprints at once.

## Patterns

### Keep Storage Canonical, Project Responses Separately

When the persisted schema has been simplified, keep `DatabaseService` focused on the canonical tables and project any API response models from those rows. In AspireAI, `Document` and `ProcessingStatus` are now projections over `files`, not evidence that `documents` or `processed_documents` tables still exist.

### Remove Sync Helpers Once the Real Callers Move

Do not leave "temporary" bridge methods behind after routers and scripts are updated. They become a second contract surface and quickly leak back into tooling and documentation.

### Give Support Scripts First-Class Canonical Helpers

If helper scripts still need to create rows or inspect processing state, add explicit canonical methods like `create_file_record()` or `get_processing_status()` rather than preserving deprecated wrappers such as `save_document()` or `get_processed_document()`.

### Update Docs and Tests in the Same Pass

Schema cleanup is incomplete until docs and verification scripts stop teaching the retired footprint. The useful AspireAI gate was:

- dependency-light contract audit (`python src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`)
- direct SQLite footprint check (`python test_database_schema.py`)

## Examples

```python
document = db.get_document_by_id(file_id)
status = db.get_processing_status(file_id)
db.update_file_status(file_id, "processed")
```

## Anti-Patterns

- **Leaving no-op sync methods in the service** — they keep the old contract alive in tooling.
- **Documenting internal processing models as persisted tables** — that misleads future maintainers.
- **Rewriting routers without updating helper scripts** — stale support scripts reintroduce drift immediately.
