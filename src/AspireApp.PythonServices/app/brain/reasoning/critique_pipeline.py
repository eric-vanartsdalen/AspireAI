"""
Critique pipeline — multi-agent reasoning flow for thorough, evidence-backed responses.

Phase 3b implementation: Uses the AgentProvider abstraction to orchestrate
Planner → Retriever → Synthesizer → Critic agents.
"""

from __future__ import annotations

import logging
from typing import Any

from ..knowledge import BrainKnowledgeRetriever
from .agent_provider import AgentProvider, AgentResponse
from ...contracts import Evidence, KnowledgeItem, ReasoningStep

logger = logging.getLogger(__name__)


class CritiquePipeline:
    """
    Multi-agent critique pipeline for deep, validated responses.

    Orchestrates: Planner → Retriever → Synthesizer → Critic
    Each agent contributes reasoning steps visible to the user.
    """

    def __init__(
        self,
        agent_provider: AgentProvider,
        knowledge_retriever: BrainKnowledgeRetriever,
    ):
        """
        Initialize critique pipeline.

        Args:
            agent_provider: Framework-agnostic agent orchestration provider
            knowledge_retriever: BRAIN knowledge retrieval service
        """
        self.agent_provider = agent_provider
        self.knowledge_retriever = knowledge_retriever

    async def execute(
        self,
        query: str,
        tenant_id: str,
        correlation_id: str,
        top_k: int = 5,
    ) -> dict[str, Any]:
        """
        Execute full critique pipeline.

        Returns:
            Dict containing:
                - answer: Final vetted response
                - confidence: Calibrated confidence score
                - evidence: Source citations
                - reasoning_steps: Traceable agent reasoning chain
                - proactive_suggestions: Related insights
        """
        if not self.agent_provider.is_available():
            raise RuntimeError("Agent provider not configured")

        reasoning_steps: list[ReasoningStep] = []
        evidence: list[Evidence] = []

        # Step 1: Planner — decompose query into sub-questions
        logger.info(f"[{correlation_id}] Critique: Planning phase")
        planner_response = await self.agent_provider.run_agent(
            agent_name="planner",
            prompt=f"Analyze this question and break it into 2-3 specific sub-queries that would help answer it thoroughly: {query}",
            context={"original_query": query},
        )
        reasoning_steps.append(
            ReasoningStep(
                step="planning",
                reasoning=planner_response.content,
                tool="planner-agent",
                result=f"Decomposed query into sub-questions",
            )
        )

        # Step 2: Retriever — query knowledge for each sub-question
        logger.info(f"[{correlation_id}] Critique: Retrieval phase")
        sub_queries = self._extract_sub_queries(planner_response.content)
        all_knowledge: list[KnowledgeItem] = []

        for i, sub_query in enumerate(sub_queries[:3], 1):  # Limit to 3 sub-queries
            try:
                knowledge_result = await self.knowledge_retriever.retrieve(
                    sub_query,
                    tenant_id=tenant_id,
                    correlation_id=correlation_id,
                    limit=top_k,
                    top_k=top_k,
                    chunk_top_k=top_k,
                    include_references=True,
                    include_chunk_content=True,
                )
                all_knowledge.extend(knowledge_result.results)
                reasoning_steps.append(
                    ReasoningStep(
                        step=f"retrieval-{i}",
                        reasoning=f"Retrieved context for: {sub_query}",
                        tool="brain-knowledge-retriever",
                        result=f"Found {len(knowledge_result.results)} results",
                    )
                )
            except Exception as e:
                logger.warning(f"Sub-query retrieval failed for '{sub_query}': {e}")
                reasoning_steps.append(
                    ReasoningStep(
                        step=f"retrieval-{i}",
                        reasoning=f"Retrieval failed for: {sub_query}",
                        tool="brain-knowledge-retriever",
                        result=f"Error: {str(e)[:100]}",
                    )
                )

        # Deduplicate and score knowledge items
        unique_knowledge = self._deduplicate_knowledge(all_knowledge)
        evidence = self._convert_to_evidence(unique_knowledge[:10])  # Top 10 sources

        # Step 3: Synthesizer — merge knowledge into coherent draft
        logger.info(f"[{correlation_id}] Critique: Synthesis phase")
        knowledge_context = self._build_knowledge_context(unique_knowledge[:10])
        synthesizer_response = await self.agent_provider.run_agent(
            agent_name="synthesizer",
            prompt=f"Using the retrieved context below, provide a comprehensive answer to: {query}",
            context={
                "query": query,
                "knowledge": knowledge_context,
            },
        )
        reasoning_steps.append(
            ReasoningStep(
                step="synthesis",
                reasoning="Merged knowledge from multiple sources into draft answer",
                tool="synthesizer-agent",
                result="Draft response generated",
            )
        )

        # Step 4: Critic — evaluate quality, check contradictions, score confidence
        logger.info(f"[{correlation_id}] Critique: Validation phase")
        critic_response = await self.agent_provider.run_agent(
            agent_name="critic",
            prompt=(
                f"Evaluate this draft answer for accuracy and completeness:\n\n"
                f"Question: {query}\n"
                f"Draft Answer: {synthesizer_response.content}\n\n"
                f"1. Are there any contradictions or gaps?\n"
                f"2. Is the answer well-supported by the evidence?\n"
                f"3. Rate confidence (0.0-1.0) based on evidence quality.\n"
                f"4. Provide a final revised answer if needed."
            ),
            context={
                "draft": synthesizer_response.content,
                "evidence_count": len(evidence),
            },
        )
        reasoning_steps.append(
            ReasoningStep(
                step="critique",
                reasoning=critic_response.content[:500],  # Truncate for brevity
                tool="critic-agent",
                result="Quality validation complete",
            )
        )

        # Extract final answer and confidence from critic
        final_answer = self._extract_final_answer(critic_response.content, synthesizer_response.content)
        confidence = self._extract_confidence(critic_response.content, unique_knowledge)

        return {
            "answer": final_answer,
            "confidence": confidence,
            "evidence": evidence,
            "reasoning_steps": reasoning_steps,
            "proactive_suggestions": [],  # Future: add proactive monitoring
        }

    def _extract_sub_queries(self, planner_output: str) -> list[str]:
        """Parse sub-queries from planner response."""
        # Simple heuristic: look for numbered lines or question marks
        lines = [line.strip() for line in planner_output.split("\n") if line.strip()]
        sub_queries = [
            line.lstrip("0123456789.-) ").strip()
            for line in lines
            if "?" in line or any(c.isdigit() for c in line[:5])
        ]
        return sub_queries if sub_queries else [planner_output[:200]]

    def _deduplicate_knowledge(self, items: list[KnowledgeItem]) -> list[KnowledgeItem]:
        """Remove duplicate knowledge items, keeping highest confidence."""
        seen: dict[str, KnowledgeItem] = {}
        for item in items:
            key = item.content[:100]  # Use content prefix as dedup key
            if key not in seen or item.confidence > seen[key].confidence:
                seen[key] = item
        return sorted(seen.values(), key=lambda x: x.confidence, reverse=True)

    def _build_knowledge_context(self, items: list[KnowledgeItem]) -> str:
        """Format knowledge items into context string for synthesis."""
        if not items:
            return "No relevant context found."

        parts = []
        for i, item in enumerate(items, 1):
            source = ", ".join(item.source_refs) if item.source_refs else "unknown"
            parts.append(f"[{i}] {item.content}\n    Source: {source} (confidence: {item.confidence:.2f})")
        return "\n\n".join(parts)

    def _convert_to_evidence(self, items: list[KnowledgeItem]) -> list[Evidence]:
        """Convert knowledge items to evidence citations."""
        return [
            Evidence(
                content=item.content[:500],
                confidence=item.confidence,
                source=", ".join(item.source_refs) if item.source_refs else "unknown",
            )
            for item in items
        ]

    def _extract_final_answer(self, critic_output: str, draft_answer: str) -> str:
        """Extract final answer from critic or fall back to draft."""
        # Look for revised answer in critic output
        if "revised answer:" in critic_output.lower():
            parts = critic_output.lower().split("revised answer:")
            if len(parts) > 1:
                return parts[1].strip()

        # Check for explicit answer section
        if "final answer:" in critic_output.lower():
            parts = critic_output.lower().split("final answer:")
            if len(parts) > 1:
                return parts[1].strip()

        # If no revision found, use synthesized draft
        return draft_answer

    def _extract_confidence(self, critic_output: str, knowledge: list[KnowledgeItem]) -> float:
        """Extract confidence score from critic evaluation."""
        # Look for explicit confidence rating in critic output
        import re

        # Try multiple patterns for confidence extraction
        patterns = [
            r"confidence[:\s]+([0-9]+\.?[0-9]*)",  # "Confidence: 0.85"
            r"([0-9]+\.?[0-9]*)\s*confidence",      # "0.85 confidence"
            r"score[:\s]+([0-9]+\.?[0-9]*)",        # "Score: 0.85"
        ]
        
        for pattern in patterns:
            confidence_match = re.search(pattern, critic_output.lower())
            if confidence_match:
                try:
                    score = float(confidence_match.group(1))
                    return min(max(score, 0.0), 1.0)  # Clamp to [0, 1]
                except ValueError:
                    continue

        # Fallback: compute from knowledge confidence scores
        if knowledge:
            scores = [k.confidence for k in knowledge if k.confidence > 0]
            if scores:
                return round(sum(scores) / len(scores), 3)

        return 0.3  # Conservative default when critique can't determine
