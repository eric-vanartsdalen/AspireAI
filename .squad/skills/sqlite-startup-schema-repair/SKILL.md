# SQLite Startup Schema Repair

Use this pattern when the Python service starts against a persisted SQLite file that may predate the latest canonical schema.

## Pattern

1. Keep the canonical `CREATE TABLE IF NOT EXISTS` statements as the source of truth for brand-new databases.
2. Immediately inspect existing tables with `PRAGMA table_info(...)`.
3. Add any missing canonical columns with `ALTER TABLE ... ADD COLUMN` before creating indexes or running queries that reference those columns.
4. For `NOT NULL` compatibility columns, use constant defaults (`''`, `0`, `'uploaded'`, `'upload'`) so older rows stay readable without a rewrite.
5. Make smoke coverage fail loudly on startup errors; do not catch and print database initialization exceptions.
6. When local and container database paths differ, resolve the path from the real environment first (repo/cwd for local runs, `/app/...` mounts in container) before attempting any schema repair.

## QA Coverage

- Test the real fallback ordering, not a mocked candidate list. Patch only environment detectors such as `_get_repository_root`, `_is_running_in_container`, and `Path.cwd`, then assert `DatabaseService()` picks the repo database before any `/app/database` fallback on Windows/local runs.
- Add one real startup-failure test with an intentionally incompatible SQLite file and assert the raised `RuntimeError` includes:
  - the failing database path
  - the original SQLite cause (for example `no such column: file_hash`)
  - schema diagnostics that identify the legacy mismatch
- Keep the smoke script aligned with the current public API (`list_documents()` instead of removed helpers) and make optional dependencies skippable so manual QA can still run on a partially provisioned workstation.

## Example In Repo

- Runtime repair: `src/AspireApp.PythonServices/app/services/database_service.py`
- Regression: `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`
- Smoke coverage: `src/AspireApp.PythonServices/test_services.py`
