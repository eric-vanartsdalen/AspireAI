# Decision: Add Missing DatabaseService Methods for Router Compatibility

**Date:** 2025-11-02
**Author:** Jarvis (Python / Data Dev)
**Status:** Implemented

## Context

Python routers (`documents.py`, `processing.py`) call 9 methods on `DatabaseService` that didn't exist, causing `AttributeError` at runtime. This was identified as P0 Item 3 in the action plan.

## Decision

Added 9 backward-compatibility wrapper methods to `DatabaseService` following the established pattern (delegation to existing file-based methods + model conversion). This approach was chosen over rewriting routers because:

1. It preserves the existing router API contract unchanged
2. It reuses proven internal methods (`get_file_by_id`, `get_unprocessed_files`, etc.)
3. It's consistent with the 3 wrapper methods already in place (`get_all_documents`, `save_document`, `update_processing_status`)

## Methods Added

| Method | Delegates To | Called By |
|--------|-------------|-----------|
| `get_document()` | `get_file_by_id()` → `_file_dict_to_document()` | processing.py, documents.py |
| `get_unprocessed_documents()` | `get_unprocessed_files()` → `_file_dict_to_document()` | processing.py, documents.py |
| `get_documents_by_status()` | Direct query with status translation | documents.py |
| `save_processed_document()` | `update_file_processing_results()` + `update_file_status()` | processing.py |
| `get_processed_document()` | `get_file_by_id()` → `ProcessedDocument()` | processing.py, documents.py |
| `get_statistics()` | `self._stats` + `ConnectionPool` internals | documents.py |
| `get_active_services()` | Static response | documents.py |
| `get_file_document_sync_status()` | `COUNT(*)` on files table | documents.py |
| `force_sync_files_and_documents()` | No-op (schema unified) | documents.py |

## Impact

- **Unblocks:** All document and processing router endpoints
- **Risk:** Low — wrapper methods only delegate to tested internals
- **No breaking changes:** Existing methods untouched
