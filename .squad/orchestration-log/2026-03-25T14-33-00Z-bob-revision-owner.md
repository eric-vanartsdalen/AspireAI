# Orchestration Log: Bob — Revision Owner — LightRAG P1

**Agent:** Bob (Lead/Architect)  
**Session:** LightRAG P1 review and revision  
**Mode:** background  
**Task:** Narrow roadmap and config claims to match achievable P1 scope  
**Timestamp:** 2026-03-25T14:33:00Z

## Outcome

**Status:** COMPLETE  
**Deliverables:** Narrowed roadmap claims + explicit Neo4JStorage and HTTP LightRAG wiring in AppHost

## Summary

Bob narrowed P1 scope after Jarvis's implementation and Buster's first review:

### Roadmap Changes

1. **Markdown Export:** ✅ CHECKED — achievable via docling/Neo4j extraction
2. **LightRAG Handoff:** ✅ CHECKED — explicit HTTP wiring in AppHost + Python service call
3. **Query Path Proof:** ❌ UNCHECKED — removed from P1 (moves to follow-up item)
4. **Retrieval Path Proof:** ❌ UNCHECKED — removed from P1 (moves to follow-up item)

### AppHost Wiring Changes

1. **Neo4JStorage Configuration:**
   - Explicitly set `NEO4J_CONNECTION_URI` to Neo4j bolt endpoint
   - Configure Neo4j service as dependency for Python workers

2. **HTTP LightRAG Endpoint:**
   - Added `LIGHTRAG_ENDPOINT` parameter (default: `http://localhost:8001`)
   - Passed to Python service via `WithEnvironment()`
   - No auto-discovery; explicit reference required

### Config Removals

Removed overly optimistic claims about:
- Automatic capability detection
- Implicit Neo4j→LightRAG bridging
- Magic query routing

## Validation

- AppHost builds without errors
- Parameters resolve correctly at runtime
- Environment variables propagate to Python service
- Contract narrowness passes Buster gate

## Propagated to

- Buster (re-review of narrowed contract)
- Coordinator verification (roadmap audit)

## Decision Log

- Narrowing prevents future regression (auto-pickup is not happening)
- Explicit wiring improves debuggability (clear endpoint references)
- P1 stays realistic (markdown export + handoff, NOT full query path)
