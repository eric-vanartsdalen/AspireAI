# Postgres upload store cutover

## When to use

Use this pattern when a .NET service in Aspire needs to move an EF Core-backed operational store from a file database to Aspire-managed PostgreSQL without rewriting the application surface.

## Pattern

1. In AppHost, create the Postgres database resource with the logical name the app actually expects (for the current AspireAI runtime: `appdb`).
2. Inject that resource into the consuming project with `.WithReference(...)` instead of hand-building `ConnectionStrings__...` values.
3. In the app project, swap the EF provider to `UseNpgsql(...)` and delete provider-specific connection helpers/interceptors.
4. Remove persistence-side cleanup that only exists for SQLite journaling or checkpoints.
5. Add one focused integration test that:
    - calls the real upload API,
    - verifies the uploaded file still lands in the shared data directory,
    - verifies the corresponding `files` row exists in Postgres via `NpgsqlConnection`.
6. In contract tests, derive the database name from `postgres.AddDatabase("...")` and assert Web/Python match it; do not hardcode a legacy literal that can outlive the real runtime.

## Python worker extension

Use the same cutover when a Python processing service reads those uploaded rows:

1. Preserve the Web-owned `files` and `document_pages` schema instead of inventing worker-specific tables.
2. Resolve Postgres from connection-string-first config, then fall back to `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DATABASE`, `POSTGRES_USER`, and `POSTGRES_PASSWORD`.
3. Replace SQLite-only behavior (`PRAGMA`, file-path DB discovery, WAL workarounds) with pooled Postgres access.
4. Keep retry semantics in the `files.status` lifecycle and make `document_pages` idempotent with `(file_id, page_number)` upserts.
5. For fast regression coverage, patch the Python pool with a fake Postgres connection layer instead of requiring a live database in every unit test run.

## AspireAI example

- AppHost: `src/AspireApp.AppHost/AppHost.cs`
- Web startup: `src/AspireApp.Web/Program.cs`
- Storage service: `src/AspireApp.Web/Shared/FileStorageService.cs`
- Regression test: `src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs`
- Python store service: `src/AspireApp.PythonServices/app/services/database_service.py`
- Python regression helpers: `src/AspireApp.PythonServices/tests/fake_postgres.py`, `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`
