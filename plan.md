# AspireAI Action Plan (Squad Review)

Last updated: 2026-02-22

## Objective

Stabilize the document upload and processing pipeline, align C#↔Python contracts, and establish a real test/CI safety net so future work can be shipped safely.

## Current Assessment

- Foundation is solid: Aspire orchestration, Blazor app structure, and core service boundaries are in place.
- The pipeline is currently blocked by contract and status mismatches between .NET and Python.
- Automated testing is effectively absent; CI does not enforce build/test quality gates.

## Priority 0 — Unblock the Pipeline ✅ COMPLETE

1. ~~**Fix `save_document_page` invocation/signature mismatch**~~ ✅ `e9d90ea`
   - Aligned caller kwargs (`file_id`, `page_number`, `content`, `metadata`, `neo4j_node_id`) with service signature.

2. ~~**Align schema naming around `document_pages` foreign key**~~ ✅ `6e5b34b` + `77db074`
   - C# `[Column("file_id")]`, Python model `file_id: int`, DB schema `file_id INTEGER NOT NULL` — all aligned.

3. ~~**Fix Python router/service contract mismatches**~~ ✅ `a8e1b71`
   - Added 9 backward-compatibility wrapper methods to DatabaseService for all router calls.

4. ~~**Normalize upload status value**~~ ✅ `62ee545`
   - FileUploadController now writes `"uploaded"` (lowercase) consistently. Python queries match.

## Priority 1 — Testing and CI (Critical Program Work)

5. **Bootstrap test infrastructure**
   - Add/verify C# test project(s).
   - Add pytest tooling for Python tests.
   - Ensure repo has clear test entry points.

6. **Add high-risk coverage first**
   - Cross-service contract tests (C# JSON ↔ Python models).
   - Upload-to-processing happy path.
   - Error paths for processing and DB operations.

7. **Enable real CI gates**
   - Update workflow to run build + tests (not placeholder commands).
   - Block merges on failed build/test.

## Priority 2 — Orchestration and Config Stability

8. **Clarify ApiService role**
   - Decide whether to remove vestigial API scaffolding or keep it with explicit near-term purpose.

9. **Resolve AI model config key mismatch**
   - Standardize one key across AppHost and Web (`AI-Model` or equivalent single canonical name).

10. **Remove/relax unnecessary LightRAG startup blocking**
   - Add a health check if it is required in startup chain, or remove `WaitFor` until integrated.

## Priority 3 — Reliability and Maintainability

11. **Replace high-impact `Console.WriteLine` usage with `ILogger<T>`**
   - Prioritize Chat and upload/processing-related paths first.

12. **Unify duplicate utility implementations**
   - Consolidate duplicate service discovery helper logic into one shared implementation.

13. **Pin Python dependency versions**
    - Lock tested versions in `requirements.txt` to make builds reproducible.

14. **Optimize Neo4j batch writes**
    - Move page/relationship creation to batched `UNWIND` patterns for scale.

## Execution Order

1. Finish Priority 0 completely.
2. Run end-to-end manual validation in Aspire dashboard.
3. Complete Priority 2 for startup/config reliability.
4. Land Priority 1 test/CI foundation before larger refactors.
5. Execute Priority 3 cleanup and performance hardening.

## Done Criteria

- Uploading a file from Web reliably appears in Python unprocessed list.
- Processing completes without contract/signature errors.
- Data writes/readbacks are consistent across C#, Python, and schema.
- CI runs build + tests automatically and fails on regressions.
- Core integration flow is covered by automated tests.
