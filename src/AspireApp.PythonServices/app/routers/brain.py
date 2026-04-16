"""
BRAIN chat router — /brain/chat endpoint for knowledge-augmented responses.

Phase 3a: Regular mode retrieves context via BrainKnowledgeRetriever,
augments the prompt with evidence, and generates via Ollama.

Phase 3b: Critique mode uses multi-agent pipeline for thorough validation.
"""

import asyncio
import logging
import time
import uuid

from fastapi import APIRouter, Depends, HTTPException

from ..brain.knowledge import BrainKnowledgeRetriever
from ..brain.reasoning import PydanticAIProvider, CritiquePipeline
from ..contracts import (
    BrainChatRequest,
    ChatMode,
    ConversationMessage,
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


def get_agent_provider(
    llm: LlmChatService = Depends(get_llm_chat_service),
) -> PydanticAIProvider:
    """Factory for PydanticAI agent provider (swappable)."""
    return PydanticAIProvider(
        model_name=llm.model_name,
        endpoint=llm.endpoint,
    )


def get_critique_pipeline(
    agent_provider: PydanticAIProvider = Depends(get_agent_provider),
    retriever: BrainKnowledgeRetriever = Depends(get_brain_retriever),
) -> CritiquePipeline:
    """Factory for critique pipeline orchestrator."""
    return CritiquePipeline(
        agent_provider=agent_provider,
        knowledge_retriever=retriever,
    )


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


def _format_conversation_history(messages: list[ConversationMessage], max_messages: int = 6) -> str:
    """Render recent chat turns into a compact history block."""
    recent_messages = messages[-max_messages:]
    rendered_messages: list[str] = []

    for message in recent_messages:
        role = message.role.strip().lower()
        content = " ".join(message.content.split())
        if role not in {"user", "assistant"} or not content:
            continue

        speaker = "User" if role == "user" else "Assistant"
        rendered_messages.append(f"{speaker}: {content}")

    return "\n".join(rendered_messages)


def _build_retrieval_query(query: str, conversation_history: list[ConversationMessage]) -> str:
    """Blend recent chat turns into the retrieval query for follow-up questions."""
    history_block = _format_conversation_history(conversation_history)
    if not history_block:
        return query

    return (
        f"Conversation history:\n{history_block}\n\n"
        f"Current user question:\n{query}"
    )


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
    critique_pipeline: CritiquePipeline = Depends(get_critique_pipeline),
) -> ReasonResponse:
    """
    Knowledge-augmented chat endpoint.

    Regular mode: retrieve context → augment prompt → generate response.
    Critique mode: multi-agent pipeline (Planner → Retriever → Synthesizer → Critic).
    """
    correlation_id = request.correlation_id or uuid.uuid4().hex

    # Route to critique pipeline if critique mode requested
    if request.mode == ChatMode.CRITIQUE:
        if not critique_pipeline.agent_provider.is_available():
            raise HTTPException(
                status_code=503,
                detail="Critique mode unavailable: agent provider not configured (check OLLAMA_ENDPOINT).",
            )

        t0 = time.monotonic()
        try:
            result = await critique_pipeline.execute(
                query=request.query,
                tenant_id=request.tenant_id,
                correlation_id=correlation_id,
                top_k=request.top_k,
                conversation_history=request.conversation_history,
            )
            duration_ms = round((time.monotonic() - t0) * 1000)
            logger.info(f"[{correlation_id}] Critique pipeline completed in {duration_ms}ms")

            return ReasonResponse(
                tenant_id=request.tenant_id,
                correlation_id=correlation_id,
                answer=result["answer"],
                confidence=result["confidence"],
                evidence=result["evidence"],
                reasoning_steps=result["reasoning_steps"],
                proactive_suggestions=result["proactive_suggestions"],
            )
        except Exception as e:
            logger.error(f"[{correlation_id}] Critique pipeline failed: {e}", exc_info=True)
            raise HTTPException(
                status_code=500,
                detail=f"Critique pipeline failed: {str(e)[:100]}",
            )

    # Regular mode path (existing implementation)
    if not llm.is_available():
        raise HTTPException(
            status_code=503,
            detail="LLM chat service is unavailable (OLLAMA_ENDPOINT not configured).",
        )

    reasoning_steps: list[ReasoningStep] = []

    # Step 1: Retrieve knowledge context
    t0 = time.monotonic()
    retrieval_query = _build_retrieval_query(request.query, request.conversation_history)
    try:
        knowledge = await retriever.retrieve(
            retrieval_query,
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
                reasoning=(
                    f"Queried knowledge graph for follow-up context using {len(request.conversation_history)} prior messages"
                    if request.conversation_history
                    else f"Queried knowledge graph for: {request.query[:80]}"
                ),
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
            conversation_history=request.conversation_history,
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
