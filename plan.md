# AspireAI Action Plan (Squad Review)

Last updated: 2026-02-22

## Objective

Stabilize the document upload and processing pipeline, align C#↔Python contracts, and establish a real test/CI safety net so future work can be shipped safely.

## Current Assessment

- Foundation is solid: Aspire orchestration, Blazor app structure, and core service boundaries are in place.
- The pipeline is currently blocked by contract and status mismatches between .NET and Python.
- Automated testing is effectively absent; CI does not enforce build/test quality gates.

## Priority 0 — Unblock the Pipeline (Do First)

1. **Normalize upload status value**
   - Update Web upload flow to write `uploaded` (lowercase) consistently.
   - Verify Python discovery queries read newly uploaded files.

2. **Fix Python router/service contract mismatches**
   - Update routers to call the DatabaseService methods that actually exist.
   - Remove calls to missing methods that currently throw runtime errors.

3. **Fix `save_document_page` invocation/signature mismatch**
   - Align caller and service signature so page persistence no longer crashes.

4. **Align schema naming around `document_pages` foreign key**
   - Resolve `file_id` vs `document_id` mismatch across C# models, Python, and DB usage.

## Priority 1 — Orchestration and Config Stability

5. **Resolve AI model config key mismatch**
   - Standardize one key across AppHost and Web (`AI-Model` or equivalent single canonical name).

6. **Remove/relax unnecessary LightRAG startup blocking**
   - Add a health check if it is required in startup chain, or remove `WaitFor` until integrated.

7. **Clarify ApiService role**
   - Decide whether to remove vestigial API scaffolding or keep it with explicit near-term purpose.

## Priority 2 — Reliability and Maintainability

8. **Replace high-impact `Console.WriteLine` usage with `ILogger<T>`**
   - Prioritize Chat and upload/processing-related paths first.

9. **Unify duplicate utility implementations**
   - Consolidate duplicate service discovery helper logic into one shared implementation.

10. **Pin Python dependency versions**
    - Lock tested versions in `requirements.txt` to make builds reproducible.

11. **Optimize Neo4j batch writes**
    - Move page/relationship creation to batched `UNWIND` patterns for scale.

## Priority 3 — Testing and CI (Critical Program Work)

12. **Bootstrap test infrastructure**
    - Add/verify C# test project(s).
    - Add pytest tooling for Python tests.
    - Ensure repo has clear test entry points.

13. **Enable real CI gates**
    - Update workflow to run build + tests (not placeholder commands).
    - Block merges on failed build/test.

14. **Add high-risk coverage first**
    - Cross-service contract tests (C# JSON ↔ Python models).
    - Upload-to-processing happy path.
    - Error paths for processing and DB operations.

## Execution Order

1. Finish Priority 0 completely.
2. Run end-to-end manual validation in Aspire dashboard.
3. Complete Priority 1 for startup/config reliability.
4. Land Priority 3 test/CI foundation before larger refactors.
5. Execute Priority 2 cleanup and performance hardening.

## Done Criteria

- Uploading a file from Web reliably appears in Python unprocessed list.
- Processing completes without contract/signature errors.
- Data writes/readbacks are consistent across C#, Python, and schema.
- CI runs build + tests automatically and fails on regressions.
- Core integration flow is covered by automated tests.
