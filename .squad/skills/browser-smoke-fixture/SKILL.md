# Browser smoke fixture

## When to use

Use this pattern when an AspireAI end-to-end browser test needs to prove upload + processing without dragging in a slow or unsupported document artifact.

## Pattern

1. Use a **tiny processable PDF** fixture for browser smoke tests.
2. Do **not** swap in `.txt`/`.md` placeholders unless the Python processing pipeline explicitly supports them end-to-end.
3. Avoid large real-world PDFs for smoke coverage; they are better suited to heavier integration or manual checks.
4. Validate the real browser gate with:
   - `python -m pytest tests\test_p0_contract_audit.py -q`
   - `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --nologo --disable-build-servers --filter "FullyQualifiedName~BasicAspireAppHostTests"`
5. If reruns start failing with locked binaries, clear stale `AspireApp.WebTest.exe` processes before rebuilding or rerunning.
6. If a fixture-backed class crashes before reporting individual tests, inspect Docker container state/logs immediately; shared `database\postgres` and `database\neo4j\data` bind mounts can block the whole Aspire stack with Postgres checkpoint corruption or Neo4j store locks.

## AspireAI example

- Stable smoke fixture: `src\AspireApp.WebTest\DataExample\processing-smoke.pdf`
- Browser gate: `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`
- Related operational proof: `src\AspireApp.WebTest\Tests\OperationalUploadStoreTests.cs`
- Harness startup: `src\AspireApp.WebTest\Fixtures\TestFixture.cs`
- Shared storage wiring: `src\AspireApp.AppHost\AppHost.cs`

## Anti-Patterns

- Assuming a browser/Playwright test body is wrong when the class never reaches an individual test result and the WebTest child process is what hung.
- Re-running fixture-backed tests against dirty repo storage without first checking for exited `postgres:*` containers or Neo4j `store_lock` errors.
