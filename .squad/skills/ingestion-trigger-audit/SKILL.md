# Ingestion Trigger Audit

Use this skill when one service writes uploaded files and another service is expected to ingest them later.

## Goal

Separate three concerns that are often conflated:

1. Physical file creation
2. Discovery of work
3. Downstream ingestion / handoff

## Audit checklist

1. Trace the upload writer. Identify exactly where the file is saved and which database row or message marks it as ready.
2. Search for automatic discovery mechanisms: startup hooks, pollers, schedulers, filesystem watchers, queue consumers, or cron-style jobs.
3. Find the real selection query for pending work (`uploaded`, `error`, etc.) and confirm whether discovery is database-driven, filesystem-driven, or message-driven.
4. Trace the explicit trigger endpoints or commands that enqueue the actual processing work.
5. Follow the processing task all the way through persistence and downstream handoff. Verify whether the next system is called automatically, synchronously, or by a separate scan step.
6. Test the unsupported path on purpose: drop a file into shared storage without creating the expected record. Record whether anything notices.
7. Update docs, tests, and plans so they describe the real trigger contract rather than an assumed one.

## Red flags

- Tests stop after verifying upload success but still describe the flow as end-to-end
- Shared-folder presence is treated as proof of ingestion
- Downstream scan / import responses are logged but not persisted
- Operators cannot see backlog counts or stale in-progress rows

## Output expectations

- Name the actual discovery mechanism in plain language
- Call out unsupported ingestion paths explicitly
- Tie query readiness to the concrete persisted outputs that retrieval depends on
