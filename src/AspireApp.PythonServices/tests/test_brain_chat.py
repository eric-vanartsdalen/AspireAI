"""Tests for /brain/chat endpoint (Phase 3a Regular mode)."""

import asyncio
import json
import pytest
from unittest.mock import AsyncMock, MagicMock, patch

from app.contracts import (
    BrainChatRequest,
    ChatMode,
    Evidence,
    KnowledgeItem,
    KnowledgeResult,
    ReasonResponse,
    ReasoningStep,
)
from app.routers.brain import (
    _build_context_block,
    _compute_confidence,
    _items_to_evidence,
    brain_chat,
)
from app.services.llm_chat_service import LlmChatService


# --- Helper factories ---

def _make_items(count: int = 2) -> list[KnowledgeItem]:
    return [
        KnowledgeItem(
            content=f"Knowledge item {i}",
            confidence=0.8 + i * 0.05,
            source_refs=[f"doc:{i}/page:1"],
            relevance_score=0.9 - i * 0.1,
        )
        for i in range(count)
    ]


def _make_request(**overrides) -> BrainChatRequest:
    defaults = {
        "tenant_id": "test-tenant",
        "correlation_id": "test-corr-001",
        "query": "What is machine learning?",
        "mode": ChatMode.REGULAR,
        "top_k": 3,
    }
    defaults.update(overrides)
    return BrainChatRequest(**defaults)


# --- Unit tests for helper functions ---

class TestBuildContextBlock:
    def test_empty_items_returns_empty(self):
        assert _build_context_block([]) == ""

    def test_formats_single_item(self):
        items = _make_items(1)
        result = _build_context_block(items)
        assert "Source 1" in result
        assert "doc:0/page:1" in result
        assert "Knowledge item 0" in result

    def test_formats_multiple_items(self):
        items = _make_items(3)
        result = _build_context_block(items)
        assert "Source 1" in result
        assert "Source 2" in result
        assert "Source 3" in result

    def test_unknown_source_when_no_refs(self):
        items = [KnowledgeItem(content="text", confidence=0.5, source_refs=[], relevance_score=0.5)]
        result = _build_context_block(items)
        assert "unknown" in result


class TestItemsToEvidence:
    def test_converts_items_to_evidence(self):
        items = _make_items(2)
        evidence = _items_to_evidence(items)
        assert len(evidence) == 2
        assert all(isinstance(e, Evidence) for e in evidence)
        assert evidence[0].source == "doc:0/page:1"

    def test_truncates_long_content(self):
        items = [KnowledgeItem(content="x" * 1000, confidence=0.5, source_refs=[], relevance_score=0.5)]
        evidence = _items_to_evidence(items)
        assert len(evidence[0].content) <= 500


class TestComputeConfidence:
    def test_no_items_returns_low_confidence(self):
        assert _compute_confidence([]) == 0.1

    def test_averages_scores(self):
        items = [
            KnowledgeItem(content="a", confidence=0.8, source_refs=[], relevance_score=0.5),
            KnowledgeItem(content="b", confidence=0.6, source_refs=[], relevance_score=0.5),
        ]
        result = _compute_confidence(items)
        assert result == 0.7

    def test_caps_at_one(self):
        items = [KnowledgeItem(content="a", confidence=1.5, source_refs=[], relevance_score=0.5)]
        result = _compute_confidence(items)
        assert result <= 1.0


# --- Endpoint integration tests (mocked dependencies) ---

class TestBrainChatEndpoint:
    def test_regular_mode_returns_reason_response(self):
        request = _make_request()
        items = _make_items(2)

        mock_retriever = AsyncMock()
        mock_retriever.retrieve.return_value = KnowledgeResult(
            tenant_id="test-tenant",
            correlation_id="test-corr-001",
            results=items,
        )

        mock_llm = MagicMock(spec=LlmChatService)
        mock_llm.is_available.return_value = True
        mock_llm.generate.return_value = "Machine learning is a subset of AI."

        mock_pipeline = MagicMock()  # Not used in regular mode

        response = asyncio.run(brain_chat(request, retriever=mock_retriever, llm=mock_llm, critique_pipeline=mock_pipeline))

        assert isinstance(response, ReasonResponse)
        assert response.answer == "Machine learning is a subset of AI."
        assert response.tenant_id == "test-tenant"
        assert response.correlation_id == "test-corr-001"
        assert len(response.evidence) == 2
        assert response.confidence > 0
        assert len(response.reasoning_steps) == 2
        assert response.reasoning_steps[0].step == "retrieval"
        assert response.reasoning_steps[1].step == "generation"

    def test_critique_mode_with_unavailable_provider_returns_503(self):
        request = _make_request(mode=ChatMode.CRITIQUE)
        mock_retriever = AsyncMock()
        mock_llm = MagicMock(spec=LlmChatService)
        mock_llm.is_available.return_value = True

        # Create mock critique pipeline with unavailable provider
        mock_pipeline = MagicMock()
        mock_pipeline.agent_provider.is_available.return_value = False

        from fastapi import HTTPException

        with pytest.raises(HTTPException) as exc_info:
            asyncio.run(brain_chat(request, retriever=mock_retriever, llm=mock_llm, critique_pipeline=mock_pipeline))
        assert exc_info.value.status_code == 503
        assert "agent provider not configured" in exc_info.value.detail.lower()

    def test_llm_unavailable_returns_503(self):
        request = _make_request()
        mock_retriever = AsyncMock()
        mock_llm = MagicMock(spec=LlmChatService)
        mock_llm.is_available.return_value = False
        mock_pipeline = MagicMock()

        from fastapi import HTTPException

        with pytest.raises(HTTPException) as exc_info:
            asyncio.run(brain_chat(request, retriever=mock_retriever, llm=mock_llm, critique_pipeline=mock_pipeline))
        assert exc_info.value.status_code == 503

    def test_retrieval_failure_falls_back_to_pure_llm(self):
        request = _make_request()

        mock_retriever = AsyncMock()
        mock_retriever.retrieve.side_effect = RuntimeError("Neo4j down")

        mock_llm = MagicMock(spec=LlmChatService)
        mock_llm.is_available.return_value = True
        mock_llm.generate.return_value = "Answer without context."

        mock_pipeline = MagicMock()

        response = asyncio.run(brain_chat(request, retriever=mock_retriever, llm=mock_llm, critique_pipeline=mock_pipeline))

        assert response.answer == "Answer without context."
        assert response.confidence == 0.1
        assert len(response.evidence) == 0
        assert "fallback" in response.reasoning_steps[0].result.lower()

    def test_llm_generation_failure_returns_502(self):
        request = _make_request()
        items = _make_items(1)

        mock_retriever = AsyncMock()
        mock_retriever.retrieve.return_value = KnowledgeResult(
            tenant_id="test-tenant",
            correlation_id="test-corr-001",
            results=items,
        )

        mock_llm = MagicMock(spec=LlmChatService)
        mock_llm.is_available.return_value = True
        mock_llm.generate.side_effect = RuntimeError("Ollama timeout")

        mock_pipeline = MagicMock()

        from fastapi import HTTPException

        with pytest.raises(HTTPException) as exc_info:
            asyncio.run(brain_chat(request, retriever=mock_retriever, llm=mock_llm, critique_pipeline=mock_pipeline))
        assert exc_info.value.status_code == 502

    def test_empty_retrieval_still_generates(self):
        request = _make_request()

        mock_retriever = AsyncMock()
        mock_retriever.retrieve.return_value = KnowledgeResult(
            tenant_id="test-tenant",
            correlation_id="test-corr-001",
            results=[],
        )

        mock_llm = MagicMock(spec=LlmChatService)
        mock_llm.is_available.return_value = True
        mock_llm.generate.return_value = "General answer."

        mock_pipeline = MagicMock()

        response = asyncio.run(brain_chat(request, retriever=mock_retriever, llm=mock_llm, critique_pipeline=mock_pipeline))

        assert response.answer == "General answer."
        assert len(response.evidence) == 0
        mock_llm.generate.assert_called_once()
        _, kwargs = mock_llm.generate.call_args
        assert kwargs.get("context") is None


class TestLlmChatService:
    def test_is_available_without_endpoint(self):
        service = LlmChatService(endpoint="")
        assert not service.is_available()

    def test_is_available_with_endpoint(self):
        service = LlmChatService(endpoint="http://localhost:11434")
        assert service.is_available()

    def test_generate_raises_without_endpoint(self):
        service = LlmChatService(endpoint="")
        with pytest.raises(RuntimeError, match="OLLAMA_ENDPOINT"):
            service.generate("hello")

    @patch("app.services.llm_chat_service.request.urlopen")
    def test_generate_calls_ollama_chat(self, mock_urlopen):
        mock_response = MagicMock()
        mock_response.read.return_value = json.dumps({
            "message": {"role": "assistant", "content": "Test response"}
        }).encode("utf-8")
        mock_response.__enter__ = MagicMock(return_value=mock_response)
        mock_response.__exit__ = MagicMock(return_value=False)
        mock_urlopen.return_value = mock_response

        service = LlmChatService(
            model_name="test-model",
            endpoint="http://localhost:11434",
        )
        result = service.generate("What is AI?", context="AI is artificial intelligence.")

        assert result == "Test response"
        mock_urlopen.assert_called_once()
        call_args = mock_urlopen.call_args
        req = call_args[0][0]
        body = json.loads(req.data.decode("utf-8"))
        assert body["model"] == "test-model"
        assert body["stream"] is False
        assert len(body["messages"]) == 2
        assert "artificial intelligence" in body["messages"][0]["content"]

    @patch("app.services.llm_chat_service.request.urlopen")
    def test_generate_without_context(self, mock_urlopen):
        mock_response = MagicMock()
        mock_response.read.return_value = json.dumps({
            "message": {"role": "assistant", "content": "Plain answer"}
        }).encode("utf-8")
        mock_response.__enter__ = MagicMock(return_value=mock_response)
        mock_response.__exit__ = MagicMock(return_value=False)
        mock_urlopen.return_value = mock_response

        service = LlmChatService(
            model_name="test-model",
            endpoint="http://localhost:11434",
        )
        result = service.generate("Hello")

        assert result == "Plain answer"
        body = json.loads(mock_urlopen.call_args[0][0].data.decode("utf-8"))
        assert "Retrieved Context" not in body["messages"][0]["content"]
