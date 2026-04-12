# FastAPI Background Processing Contract

## Use when

Apply this pattern when a FastAPI endpoint queues a `BackgroundTasks` worker and another client or test polls for completion.

## Pattern

1. Validate that the work item exists and is not already `processed` or already `processing`.
2. Persist the transition to `status='processing'` before the trigger endpoint returns.
3. Queue the background task with a flag or helper so the worker does not perform the same transition twice.
4. Back the polling endpoint with durable storage, not in-memory task state.
5. Include proof-oriented fields derived from persisted artifacts, such as page counts or staged output flags.

## Why

If the POST returns before the record is marked `processing`, immediate pollers can still observe `uploaded` and produce flaky end-to-end tests. Durable proof fields like `processed_pages` are stronger contracts than timestamps alone because they confirm a real side effect landed in storage.

## AspireAI Example

- Trigger: `POST /processing/process-document/{id}`
- Poll: `GET /processing/status/{id}`
- Durable proof: `processed_pages` counted from `document_pages`
