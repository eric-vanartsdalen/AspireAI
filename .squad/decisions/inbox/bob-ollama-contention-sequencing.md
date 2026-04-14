# Ollama Contention: Serialize Pipeline Workloads — Bob — 2026-04-18

**Author:** Bob (Lead / Architect)
**Status:** IMPLEMENTED
**Scope:** Processing pipeline ordering in `process_document_task`

## Context

FlowEndToEnd and LiveLightRagNeo4jQueryRoundTrip tests were timing out during processing. Root cause: `process_document_task` triggered LightRAG ingestion (which calls Ollama for LLM + embeddings) *before* completing its own Ollama embedding work (page + claim vectors). Both consumers competed for a single Ollama instance configured with `MAX_ASYNC=1`. The serial queuing pushed total processing time past the 2-minute test polling window.

## Decision

**Defer LightRAG handoff until after all Python-side Ollama embedding work completes.** This is a pure operation reorder — no logic or interface changes. The metadata dict still accumulates identically; it's persisted to disk slightly later in the pipeline.

## Rationale

- Ollama serves one request at a time; concurrent consumers create a serial queue.
- Each embedding batch call has a 60-second timeout; queuing behind LightRAG LLM calls can exceed this.
- Sequencing eliminates the contention window entirely.

## Architectural Rule

When multiple pipeline stages share a single-instance AI model server (Ollama), orchestrate them sequentially. This applies to any future processing step that calls Ollama — do not add concurrent Ollama consumers without increasing `MAX_ASYNC` or adding model-level isolation.

## Files Changed

- `src/AspireApp.PythonServices/app/routers/processing.py`
