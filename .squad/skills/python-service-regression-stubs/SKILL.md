# Python Service Regression Stubs

## When to use this

Use this pattern when you need Python regression coverage in AspireAI but the local workstation does not have FastAPI, Pydantic, or other heavy service dependencies installed.

## Pattern

1. Add `src\AspireApp.PythonServices` to `sys.path` inside the test.
2. Install lightweight stub modules into `sys.modules` for imports such as `pydantic`, `fastapi`, or service adapters.
3. Purge cached `app` modules before re-importing so the stubs actually take effect.
4. For `DatabaseService`, always close and clear `DatabaseService._pools` between tests.
5. Create scratch SQLite files under a repo-local test folder (for example `tests\_scratch_*`) and remove them in cleanup.

## Why it works

This keeps regression tests dependency-free and fast while still exercising the real AspireAI module code. It also avoids polluting shared developer databases or relying on OS temp directories.

## Example targets

- `app.services.database_service`
- `app.routers.processing`
- small router/task orchestration flows that only need fake collaborators
