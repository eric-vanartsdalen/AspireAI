"""
BRAIN chat router — /brain/chat endpoint for knowledge-augmented responses.

Phase 3a: Regular mode retrieves context via BrainKnowledgeRetriever,
augments the prompt with evidence, and generates via Ollama.
"""

import asyncio
import logging
import time
import uuid

from fastapi import APIRouter, Depends, HTTPException

from ..brain.knowledge import BrainKnowledgeRetriever
from ..contracts import (
    BrainChatRequest,
    ChatMode,
    Evidence,
    KnowledgeItem,
    ReasonResponse,
    ReasoningStep,
)
from ..services.embedding_service import EmbeddingService
from ..services.llm_chat_service import LlmChatService
from ..services.neo4j_service import Neo4jService

router = APIRouter(prefix="/brain", tags=["brain"])
logger = logging.getLogger(__name__)


def get_neo4j_service() -> Neo4jService:
    return Neo4jService()


def get_embedding_service() -> EmbeddingService:
    return EmbeddingService()


def get_llm_chat_service() -> LlmChatService:
    return LlmChatService()


def get_brain_retriever(
    neo4j: Neo4jService = Depends(get_neo4j_service),
    embedding: EmbeddingService = Depends(get_embedding_service),
) -> BrainKnowledgeRetriever:
    return BrainKnowledgeRetriever(neo4j_service=neo4j, embedding_service=embedding)


def _build_context_block(items: list[KnowledgeItem]) -> str:
    """Format retrieved knowledge items into a context string for the LLM."""
    if not items:
        return ""

    parts: list[str] = []
    for i, item in enumerate(items, 1):
        source_label = ", ".join(item.source_refs) if item.source_refs else "unknown"
        parts.append(
            f"[Source {i}: {source_label} | confidence={item.confidence:.2f}]\n"
            f"{item.content}"
        )
    return "\n\n".join(parts)


def _items_to_evidence(items: list[KnowledgeItem]) -> list[Evidence]:
    """Convert knowledge items to evidence citations."""
    evidence: list[Evidence] = []
    for item in items:
        source = ", ".join(item.source_refs) if item.source_refs else "unknown"
        evidence.append(
            Evidence(
                content=item.content[:500],
                confidence=item.confidence,
                source=source,
            )
        )
    return evidence


def _compute_confidence(items: list[KnowledgeItem]) -> float:
    """Compute aggregate confidence from retrieval results."""
    if not items:
        return 0.1
    scores = [item.confidence for item in items if item.confidence > 0]
    if not scores:
        return 0.2
    return round(min(sum(scores) / len(scores), 1.0), 3)


@router.post("/chat", response_model=ReasonResponse)
async def brain_chat(
    request: BrainChatRequest,
    retriever: BrainKnowledgeRetriever = Depends(get_brain_retriever),
    llm: LlmChatService = Depends(get_llm_chat_service),
) -> ReasonResponse:
    """
    Knowledge-augmented chat endpoint.

    Regular mode: retrieve context → augment prompt → generate response.
    Critique mode: returns 501 until Phase 3b agent framework is ready.
    """
    correlation_id = request.correlation_id or uuid.uuid4().hex

    if request.mode == ChatMode.CRITIQUE:
        raise HTTPException(
            status_code=501,
            detail="Critique mode requires the Phase 3b agent framework. Use regular mode.",
        )

    if not llm.is_available():
        raise HTTPException(
            status_code=503,
            detail="LLM chat service is unavailable (OLLAMA_ENDPOINT not configured).",
        )

    reasoning_steps: list[ReasoningStep] = []

    # Step 1: Retrieve knowledge context
    t0 = time.monotonic()
    try:
        knowledge = await retriever.retrieve(
            request.query,
            tenant_id=request.tenant_id,
            correlation_id=correlation_id,
            limit=request.top_k,
            top_k=request.top_k,
            chunk_top_k=request.top_k,
            include_references=True,
            include_chunk_content=True,
        )
        retrieval_ms = round((time.monotonic() - t0) * 1000)
        items = knowledge.results
        reasoning_steps.append(
            ReasoningStep(
                step="retrieval",
                reasoning=f"Queried knowledge graph for: {request.query[:80]}",
                tool="brain-knowledge-retriever",
                result=f"{len(items)} results in {retrieval_ms}ms",
            )
        )
    except Exception as e:
        logger.warning(f"Knowledge retrieval failed, proceeding without context: {e}")
        items = []
        reasoning_steps.append(
            ReasoningStep(
                step="retrieval",
                reasoning=f"Knowledge retrieval failed: {e}",
                tool="brain-knowledge-retriever",
                result="0 results (fallback to pure LLM)",
            )
        )

    # Step 2: Build augmented prompt and generate response
    context_block = _build_context_block(items)
    t1 = time.monotonic()
    try:
        answer = await asyncio.to_thread(
            llm.generate,
            request.query,
            context=context_block if context_block else None,
        )
        generation_ms = round((time.monotonic() - t1) * 1000)
        reasoning_steps.append(
            ReasoningStep(
                step="generation",
                reasoning="Generated response from augmented prompt"
                if context_block
                else "Generated response without retrieval context",
                tool="ollama",
                result=f"Response generated in {generation_ms}ms",
            )
        )
    except RuntimeError as e:
        logger.error(f"LLM generation failed: {e}")
        raise HTTPException(status_code=502, detail=f"LLM generation failed: {e}")

    # Step 3: Package response
    evidence = _items_to_evidence(items)
    confidence = _compute_confidence(items)

    return ReasonResponse(
        tenant_id=request.tenant_id,
        correlation_id=correlation_id,
        answer=answer,
        confidence=confidence,
        evidence=evidence,
        reasoning_steps=reasoning_steps,
        proactive_suggestions=[],
    )
