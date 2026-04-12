---
name: "dependency-free-python-contract-audit"
description: "Audit Python service contracts with stdlib-only tests when FastAPI/Pydantic/docling are unavailable"
domain: "testing"
confidence: "high"
source: "manual"
---

## Context

AspireAI's Python environment is not always bootstrapped when QA work starts. In that state, importing `fastapi`, `pydantic`, or `docling` fails, but we still need fast feedback on contract drift in `DatabaseService` and `DoclingService`.

## Patterns

### Stub Third-Party Imports

For contract-oriented tests, inject lightweight fake modules into `sys.modules` before importing the target module. A tiny fake `BaseModel` plus a fake `DocumentConverter` is enough to exercise path resolution and schema logic without installing the full Python stack.

### Purge Cached App Modules Between Tests

Delete cached `app.*` modules before each import cycle so the test doubles actually take effect. Otherwise Python reuses already-imported modules and your fakes never bind.

### Use `unittest.expectedFailure` for Known QA Gaps

When the goal is to document a blocking defect without turning the suite red, encode the desired behavior as an expected failure. In AspireAI this worked well for:

- joining `file_path` (directory) with `file_name` (stored filename)
- rejecting raw Windows host paths inside the Linux container path resolver

Once the implementation lands, convert those checks to ordinary passing assertions immediately. Leaving them as `expectedFailure` after the fix hides regressions from the QA gate.

### Promote Fixed Contract Checks to Hard Gates

After the implementation is corrected, remove the `expectedFailure` wrapper and keep the same test flow as a normal passing assertion. AspireAI's upload-path audit is the model: the same dependency-free harness now fails hard if either container-path joining or Windows host-path remapping regresses.

### Validate the Minimal DB Contract Directly with SQLite

Use a temporary SQLite database and inspect `sqlite_master` to confirm only the intended tables exist. This is the quickest way to validate footprint minimization for `files` and `document_pages`.

### Prefer Dict-Based Inputs When the Public Helper Accepts Them

If a contract helper accepts either a Pydantic model or a plain dictionary, use the dictionary form in stdlib-only validation. That avoids having to emulate every model method just to test path resolution or schema translation.

### Use a Temporary Stub Module for Thin Helper-Script Checks

If an operational script only imports `DatabaseService` through lightweight Pydantic models, add a temporary directory to `PYTHONPATH` with a tiny `pydantic.py` that exposes `BaseModel`. That is enough to smoke-test helpers such as `fix_schema.py`, `diagnose_database.py`, or `test_concurrent_access.py` without globally installing packages or mutating the shared machine state.

### Smoke Optional Dependencies Through the Runtime Factory

When production code already routes optional dependencies through a factory, point smoke tests at the factory instead of the heavyweight package module. In AspireAI, `app.services.service_factory` is the contract: it returns the full `DoclingService` when `docling` is installed and `docling_service_fallback` otherwise, so the smoke test can prove the active processor boots in both environments.

### Keep Required Drivers Lazy Enough for Fake-Backed Smoke Tests

If a test swaps the live database pool with `fake_postgres.FakeConnectionPool`, the module under test still needs to import cleanly before the patch lands. In AspireAI, `app.services.database_service` should tolerate a missing `psycopg_pool` import at module load and only raise when the real `ConnectionPool` is instantiated.

### Bootstrap `sys.path` Inside Standalone Test Entry Points

Some AspireAI Python checks are still useful as direct `python path\\to\\test_file.py` commands. For those files, add `src\\AspireApp.PythonServices` (and the local `tests` folder when needed) to `sys.path` before importing `app.*` so the same test file works both under `pytest` and as a script.

## Examples

```python
with _patched_dependencies():
    from app.services.database_service import DatabaseService

    service = DatabaseService(db_path=str(Path(temp_dir) / "data-resources.db"))
    resolved = service.resolve_upload_path(
        {
            "file_path": str(Path(temp_dir) / "data" / "uploads"),
            "file_name": "stored.pdf",
        }
    )
    assert resolved == Path(temp_dir) / "data" / "uploads" / "stored.pdf"
```

## Anti-Patterns

- **Requiring full dependency install for contract checks** — too slow and often impossible in offline QA sessions.
- **Leaving temp SQLite files open on Windows** — always close connection pools before deleting temp directories.
- **Encoding current broken behavior as passing assertions** — use expected failures for defects you intend to block on.
- **Keeping `expectedFailure` after the bug is fixed** — that turns a real regression gate into documentation only.
- **Installing missing Python packages globally just to run a helper script** — prefer an isolated stub or throwaway environment first.
