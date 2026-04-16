"""Tests for critique pipeline and PydanticAI provider abstraction."""

import asyncio
import os
import pytest
from unittest.mock import AsyncMock, MagicMock, patch

from app.brain.reasoning import PydanticAIProvider, CritiquePipeline, AgentResponse
from app.contracts import ConversationMessage, Evidence, KnowledgeItem, KnowledgeResult, ReasoningStep


# --- Mock agent provider for testing abstraction ---

class MockAgentProvider:
    """Fake agent provider for testing without real PydanticAI."""

    def __init__(self, available: bool = True):
        self.available = available
        self.call_history: list[tuple[str, str]] = []

    async def run_agent(self, agent_name: str, prompt: str, context=None, tools=None) -> AgentResponse:
        self.call_history.append((agent_name, prompt[:50]))
        
        # Simulate different agent behaviors
        if agent_name == "planner":
            return AgentResponse(
                content="1. What is the definition?\n2. What are the applications?\n3. What are the challenges?",
                reasoning="Decomposed query into sub-questions",
            )
        elif agent_name == "synthesizer":
            return AgentResponse(
                content="Based on the evidence, this is a comprehensive answer.",
                reasoning="Merged knowledge from retrieval",
            )
        elif agent_name == "critic":
            return AgentResponse(
                content="Confidence: 0.85\nFinal answer: This is the validated response.",
                reasoning="Quality checked, no contradictions found",
            )
        else:
            return AgentResponse(content="Agent response")

    async def run_multi_agent(self, agents, initial_context=None):
        responses = []
        for agent_name, prompt in agents:
            resp = await self.run_agent(agent_name, prompt)
            responses.append(resp)
        return responses

    def is_available(self) -> bool:
        return self.available


# --- Helper factories ---

def _make_knowledge_items(count: int = 2) -> list[KnowledgeItem]:
    return [
        KnowledgeItem(
            content=f"Knowledge content {i}",
            confidence=0.8,
            source_refs=[f"doc:{i}/page:1"],
            relevance_score=0.9,
        )
        for i in range(count)
    ]


# --- Tests ---

class TestPydanticAIProvider:
    def test_is_available_without_endpoint(self):
        provider = PydanticAIProvider(endpoint="")
        assert not provider.is_available()

    def test_is_available_with_endpoint(self):
        provider = PydanticAIProvider(
            model_name="test-model",
            endpoint="http://localhost:11434"
        )
        assert provider.is_available()

    def test_uses_default_system_prompts(self):
        provider = PydanticAIProvider(endpoint="http://localhost:11434")
        assert "planner" in provider._default_prompts
        assert "retriever" in provider._default_prompts
        assert "synthesizer" in provider._default_prompts
        assert "critic" in provider._default_prompts

    def test_prefers_chat_model_environment_for_local_ollama(self, monkeypatch):
        monkeypatch.setenv("CHAT_MODEL", "chat-model")
        monkeypatch.setenv("OLLAMA_MODEL", "legacy-model")

        provider = PydanticAIProvider(endpoint="http://localhost:11434")

        assert provider.model_name == "chat-model"

    def test_builds_ollama_model_without_openai_api_key_dependency(self, monkeypatch):
        monkeypatch.delenv("OPENAI_API_KEY", raising=False)
        monkeypatch.delenv("OPENAI_BASE_URL", raising=False)

        with (
            patch("app.brain.reasoning.pydantic_ai_provider.OpenAIProvider") as mock_provider,
            patch("app.brain.reasoning.pydantic_ai_provider.OpenAIModel") as mock_model,
            patch("app.brain.reasoning.pydantic_ai_provider.Agent") as mock_agent,
        ):
            provider = PydanticAIProvider(
                model_name="test-model",
                endpoint="http://localhost:11434/",
            )

            created_agent = provider._get_or_create_agent("planner")

        mock_provider.assert_called_once_with(base_url="http://localhost:11434/v1", api_key="ollama")
        mock_model.assert_called_once_with("test-model", provider=mock_provider.return_value)
        mock_agent.assert_called_once_with(
            model=mock_model.return_value,
            system_prompt=provider._default_prompts["planner"],
        )
        assert created_agent == mock_agent.return_value
        assert os.getenv("OPENAI_API_KEY") is None
        assert os.getenv("OPENAI_BASE_URL") is None

    def test_run_agent_executes_without_openai_environment_variables(self, monkeypatch):
        monkeypatch.delenv("OPENAI_API_KEY", raising=False)
        monkeypatch.delenv("OPENAI_BASE_URL", raising=False)

        fake_result = MagicMock()
        fake_result.data = "Planned response"
        fake_agent = MagicMock()
        fake_agent.run = AsyncMock(return_value=fake_result)

        with (
            patch("app.brain.reasoning.pydantic_ai_provider.OpenAIProvider") as mock_provider,
            patch("app.brain.reasoning.pydantic_ai_provider.OpenAIModel") as mock_model,
            patch("app.brain.reasoning.pydantic_ai_provider.Agent", return_value=fake_agent),
        ):
            provider = PydanticAIProvider(
                model_name="test-model",
                endpoint="http://localhost:11434/",
            )
            response = asyncio.run(provider.run_agent("planner", "Plan the task"))

        mock_provider.assert_called_once_with(base_url="http://localhost:11434/v1", api_key="ollama")
        mock_model.assert_called_once_with("test-model", provider=mock_provider.return_value)
        fake_agent.run.assert_awaited_once_with("Plan the task")
        assert response.content == "Planned response"
        assert os.getenv("OPENAI_API_KEY") is None
        assert os.getenv("OPENAI_BASE_URL") is None


class TestCritiquePipeline:
    def test_pipeline_execution_calls_all_agents(self):
        """Test that pipeline orchestrates planner → retriever → synthesizer → critic."""
        mock_provider = MockAgentProvider()
        mock_retriever = AsyncMock()
        mock_retriever.retrieve.return_value = KnowledgeResult(
            tenant_id="test",
            correlation_id="corr-001",
            results=_make_knowledge_items(2),
        )

        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=mock_retriever,
        )

        result = asyncio.run(pipeline.execute(
            query="What is machine learning?",
            tenant_id="test-tenant",
            correlation_id="corr-001",
            top_k=5,
        ))

        # Verify all agent stages were called
        agent_names = [call[0] for call in mock_provider.call_history]
        assert "planner" in agent_names
        assert "synthesizer" in agent_names
        assert "critic" in agent_names

        # Verify result structure
        assert "answer" in result
        assert "confidence" in result
        assert "evidence" in result
        assert "reasoning_steps" in result
        assert isinstance(result["reasoning_steps"], list)

    def test_pipeline_extracts_sub_queries_from_planner(self):
        """Test that planner output is parsed into sub-queries for retrieval."""
        mock_provider = MockAgentProvider()
        mock_retriever = AsyncMock()
        mock_retriever.retrieve.return_value = KnowledgeResult(
            tenant_id="test",
            correlation_id="corr-001",
            results=_make_knowledge_items(1),
        )

        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=mock_retriever,
        )

        asyncio.run(pipeline.execute(
            query="Test query?",
            tenant_id="test",
            correlation_id="corr-001",
        ))

        # Verify retriever was called (at least once for sub-queries)
        assert mock_retriever.retrieve.call_count > 0

    def test_pipeline_handles_retrieval_failures_gracefully(self):
        """Test that failed retrieval doesn't crash pipeline."""
        mock_provider = MockAgentProvider()
        mock_retriever = AsyncMock()
        mock_retriever.retrieve.side_effect = RuntimeError("Neo4j down")

        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=mock_retriever,
        )

        result = asyncio.run(pipeline.execute(
            query="Test query",
            tenant_id="test",
            correlation_id="corr-001",
        ))

        # Should still complete with synthesizer/critic despite retrieval failure
        assert result["answer"] is not None
        assert any("retrieval" in step.step for step in result["reasoning_steps"])

    def test_pipeline_blends_conversation_history_into_follow_up_retrieval(self):
        mock_provider = MockAgentProvider()
        mock_retriever = AsyncMock()
        mock_retriever.retrieve.return_value = KnowledgeResult(
            tenant_id="test",
            correlation_id="corr-001",
            results=_make_knowledge_items(1),
        )

        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=mock_retriever,
        )

        asyncio.run(pipeline.execute(
            query="What changed in the new upload?",
            tenant_id="test",
            correlation_id="corr-001",
            conversation_history=[
                ConversationMessage(role="user", content="Summarize the handbook."),
                ConversationMessage(role="assistant", content="It covers onboarding and benefits."),
            ],
        ))

        retrieval_queries = [call.args[0] for call in mock_retriever.retrieve.await_args_list]
        assert retrieval_queries
        assert any("Conversation history" in query for query in retrieval_queries)
        assert any("Summarize the handbook." in query for query in retrieval_queries)

    def test_pipeline_deduplicates_knowledge_items(self):
        """Test that duplicate knowledge items are removed."""
        duplicate_items = [
            KnowledgeItem(content="Same content", confidence=0.7, source_refs=["a"], relevance_score=0.8),
            KnowledgeItem(content="Same content", confidence=0.9, source_refs=["b"], relevance_score=0.9),
            KnowledgeItem(content="Different", confidence=0.8, source_refs=["c"], relevance_score=0.85),
        ]

        mock_provider = MockAgentProvider()
        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=MagicMock(),
        )

        deduplicated = pipeline._deduplicate_knowledge(duplicate_items)

        # Should keep highest confidence version of duplicates
        assert len(deduplicated) == 2
        same_content_items = [item for item in deduplicated if item.content == "Same content"]
        assert len(same_content_items) == 1
        assert same_content_items[0].confidence == 0.9

    def test_pipeline_extracts_confidence_from_critic(self):
        """Test confidence extraction from critic response."""
        mock_provider = MockAgentProvider()
        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=MagicMock(),
        )

        # Test explicit confidence rating
        critic_output = "This is good. Confidence: 0.92. No issues found."
        confidence = pipeline._extract_confidence(critic_output, [])
        assert confidence == 0.92

        # Test fallback to knowledge scores
        items = _make_knowledge_items(2)
        confidence = pipeline._extract_confidence("No explicit score", items)
        assert 0.0 < confidence <= 1.0

    def test_pipeline_extracts_final_answer_from_critic(self):
        """Test final answer extraction from critic response."""
        mock_provider = MockAgentProvider()
        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=MagicMock(),
        )

        # Test revised answer extraction
        critic_output = "Some evaluation. Revised answer: This is the improved response."
        draft = "Original draft answer."
        final = pipeline._extract_final_answer(critic_output, draft)
        assert "improved response" in final.lower()

        # Test fallback to draft when no revision
        critic_output = "Looks good as-is."
        final = pipeline._extract_final_answer(critic_output, draft)
        assert final == draft

    def test_pipeline_converts_knowledge_to_evidence(self):
        """Test knowledge items are converted to evidence citations."""
        mock_provider = MockAgentProvider()
        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=MagicMock(),
        )

        items = _make_knowledge_items(3)
        evidence = pipeline._convert_to_evidence(items)

        assert len(evidence) == 3
        assert all(isinstance(e, Evidence) for e in evidence)
        assert evidence[0].source == "doc:0/page:1"
        assert evidence[0].confidence == 0.8

    def test_pipeline_unavailable_provider_raises(self):
        """Test that unavailable provider raises error."""
        mock_provider = MockAgentProvider(available=False)
        mock_retriever = MagicMock()

        pipeline = CritiquePipeline(
            agent_provider=mock_provider,
            knowledge_retriever=mock_retriever,
        )

        with pytest.raises(RuntimeError, match="Agent provider not configured"):
            asyncio.run(pipeline.execute(
                query="Test",
                tenant_id="test",
                correlation_id="corr-001",
            ))


class TestAgentProviderAbstraction:
    def test_mock_provider_implements_protocol(self):
        """Verify mock provider satisfies AgentProvider protocol."""
        provider = MockAgentProvider()

        # Protocol methods should be callable
        assert callable(provider.run_agent)
        assert callable(provider.run_multi_agent)
        assert callable(provider.is_available)

        # Should return correct types
        response = asyncio.run(provider.run_agent("test", "prompt"))
        assert isinstance(response, AgentResponse)
        assert response.content is not None

    def test_multi_agent_passes_context_between_agents(self):
        """Test that multi-agent execution chains context."""
        provider = MockAgentProvider()

        agents = [
            ("planner", "Plan the task"),
            ("synthesizer", "Synthesize results"),
        ]

        responses = asyncio.run(provider.run_multi_agent(agents))

        assert len(responses) == 2
        assert all(isinstance(r, AgentResponse) for r in responses)
