# Web upload smoke tests

## When to use

Use this pattern when a Web/Blazor regression test needs to prove upload plus downstream processing without waiting on a large document conversion.

## Pattern

1. Keep one focused API regression that proves upload metadata lands in the shared store (`files` / `document_pages` contract).
2. Use a **small, processable PDF** fixture for the UI-driven end-to-end smoke path so the browser test exercises the same file type the Python pipeline must actually process.
3. Make sure the Blazor `<InputFile accept=...>` list matches the backend controller extension allow-list exactly.
4. Treat an API-backed empty upload list after the UI click as a hard regression; do not accept UI-only evidence that the upload button appeared to work.
5. Close every Playwright `IPage` opened by the smoke tests before fixture shutdown; xUnit v3 can surface teardown hangs as a fatal browser-suite crash even when the test body already passed.
6. In Blazor `InteractiveServer`, do not render the real `<InputFile>` until the first interactive render has completed; otherwise Playwright (and fast human clicks) can select a file before Blazor wires the change handler, leaving the upload button stuck disabled.
7. When `FlowEndToEnd` polls the Python `processing/status/{id}` endpoint, treat per-request `HttpClient.Timeout` cancellations and transient 404/DB-startup failures as retryable inside the overall processing timeout window. Fail on final timeout or explicit `error` status, not on the first cold-start stall.

## AspireAI example

- UI accept list: `src\AspireApp.Web\Components\Pages\UploadData.razor`
- Interactive readiness gate: `src\AspireApp.Web\Components\Pages\UploadData.razor.cs`
- Backend allow-list: `src\AspireApp.Web\Controllers\FileUploadController.cs`
- Smoke fixture: `src\AspireApp.WebTest\DataExample\processing-smoke.pdf`
- Smoke test: `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`

## Anti-patterns

- Swapping the browser smoke fixture to `.txt` or another easier format just to get a green run while the real processing pipeline still targets PDFs.
- Approving a browser upload flow when `GET /api/FileUpload` never exposes the uploaded file after the UI action.
- Rendering a live `input[type="file"]` during prerender and assuming the first selection will always reach the Blazor event handler.
