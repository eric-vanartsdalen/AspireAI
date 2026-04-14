from __future__ import annotations

import asyncio
import sys
import unittest
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.brain.knowledge.retrievers import (
    BrainKnowledgeRetriever,
    LightRagRetriever,
    SemanticKnowledgeRetriever,
)
from app.contracts import IKnowledgeRetriever, KnowledgeItem, KnowledgeResult


class FakeLightRagQueryService:
    def __init__(self, payload):
        self.payload = payload
        self.last_request = None

    def query_data(self, query_request):
        self.last_request = query_request
        return self.payload


class FakeNeo4jService:
    def __init__(self, results):
        self.results = results
        self.calls = []

    def search_similar_content(self, query, limit=10):
        self.calls.append({"query": query, "limit": limit})
        return list(self.results)


class CapturingRetriever(IKnowledgeRetriever):
    def __init__(self, response=None, error=None):
        self.calls = []
        self._response = response
        self._error = error

    async def retrieve(
        self,
        query: str,
        *,
        tenant_id: str = "default",
        correlation_id: str | None = None,
        limit: int = 10,
        **options,
    ) -> KnowledgeResult:
        self.calls.append(
            {
                "query": query,
                "tenant_id": tenant_id,
                "correlation_id": correlation_id,
                "limit": limit,
                "options": options,
            }
        )
        if self._error is not None:
            raise self._error
        return self._response or KnowledgeResult(
            tenant_id=tenant_id,
            correlation_id=correlation_id or "generated-correlation",
            results=[],
        )


class KnowledgeRetrieverTests(unittest.TestCase):
    def test_lightrag_retriever_shapes_chunks_into_contract_items(self):
        payload = {
            "status": "ok",
            "data": {
                "chunks": [
                    {
                        "content": "Recovered page text.",
                        "metadata": {
                            "score": 0.87,
                            "source_refs": ["doc-9#page-2"],
                        },
                        "document_id": 9,
                        "page_number": 2,
                        "filename": "doc.pdf",
                    }
                ]
            },
        }
        retriever = LightRagRetriever(query_service=FakeLightRagQueryService(payload))

        result = asyncio.run(
            retriever.retrieve(
                "where is the clause",
                tenant_id="tenant-x",
                correlation_id="corr-x",
                limit=5,
            )
        )

        self.assertEqual("tenant-x", result.tenant_id)
        self.assertEqual("corr-x", result.correlation_id)
        self.assertEqual(1, len(result.results))
        item = result.results[0]
        self.assertEqual("Recovered page text.", item.content)
        self.assertEqual(0.87, item.confidence)
        self.assertEqual(0.87, item.relevance_score)
        self.assertIn("doc-9#page-2", item.source_refs)

    def test_lightrag_retriever_falls_back_to_response_text(self):
        payload = {"data": {"response": "Fallback response."}}
        retriever = LightRagRetriever(query_service=FakeLightRagQueryService(payload))

        result = asyncio.run(retriever.retrieve("what is policy", limit=3))

        self.assertEqual(1, len(result.results))
        item = result.results[0]
        self.assertEqual("Fallback response.", item.content)
        self.assertEqual(0.5, item.confidence)

    def test_lightrag_retriever_uses_source_confidence_for_fallback_items(self):
        payload = {
            "results": [
                {
                    "content": "Recovered page text.",
                    "source_confidence": 0.91,
                    "document_id": 9,
                    "page_number": 2,
                    "filename": "doc.pdf",
                }
            ]
        }
        retriever = LightRagRetriever(query_service=FakeLightRagQueryService(payload))

        result = asyncio.run(retriever.retrieve("where is the clause", limit=5))

        self.assertEqual(1, len(result.results))
        item = result.results[0]
        self.assertEqual("Recovered page text.", item.content)
        self.assertEqual(0.91, item.confidence)
        self.assertEqual(0.91, item.relevance_score)
        self.assertIn("document:9/page:2", item.source_refs)
        self.assertIn("file:doc.pdf", item.source_refs)

    def test_semantic_knowledge_retriever_shapes_search_results_into_contract_items(self):
        neo4j = FakeNeo4jService(
            [
                {
                    "content": "Aspire AppHost coordinates the web and API projects.",
                    "document_id": 7,
                    "page_number": 2,
                    "filename": "guide.pdf",
                    "score": 0.63,
                }
            ]
        )
        retriever = SemanticKnowledgeRetriever(neo4j)

        result = asyncio.run(
            retriever.retrieve(
                "Aspire",
                tenant_id="tenant-a",
                correlation_id="corr-semantic",
                limit=3,
            )
        )

        self.assertEqual([{"query": "Aspire", "limit": 3}], neo4j.calls)
        self.assertEqual("tenant-a", result.tenant_id)
        self.assertEqual("corr-semantic", result.correlation_id)
        self.assertEqual(1, len(result.results))
        self.assertEqual(0.63, result.results[0].confidence)
        self.assertEqual(0.63, result.results[0].relevance_score)
        self.assertEqual(
            ["document:7/page:2", "file:guide.pdf"],
            result.results[0].source_refs,
        )

    def test_brain_knowledge_retriever_returns_lightrag_results_without_fallback(self):
        light_rag = CapturingRetriever(
            KnowledgeResult(
                tenant_id="tenant-a",
                correlation_id="corr-light",
                results=[
                    KnowledgeItem(
                        content="Primary LightRAG result",
                        confidence=0.72,
                        source_refs=["document:1/page:1"],
                        relevance_score=0.72,
                    )
                ],
            )
        )
        semantic = CapturingRetriever()
        retriever = BrainKnowledgeRetriever(light_rag_retriever=light_rag, semantic_retriever=semantic)

        result = asyncio.run(
            retriever.retrieve(
                "Aspire",
                tenant_id="tenant-a",
                correlation_id="corr-light",
                limit=4,
            )
        )

        self.assertEqual("Primary LightRAG result", result.results[0].content)
        self.assertEqual(1, len(light_rag.calls))
        self.assertEqual([], semantic.calls)

    def test_brain_knowledge_retriever_falls_back_to_semantic_when_lightrag_returns_empty(self):
        light_rag = CapturingRetriever(
            KnowledgeResult(
                tenant_id="tenant-a",
                correlation_id="corr-light-empty",
                results=[],
            )
        )
        semantic = CapturingRetriever(
            KnowledgeResult(
                tenant_id="tenant-a",
                correlation_id="corr-light-empty",
                results=[
                    KnowledgeItem(
                        content="Semantic fallback result",
                        confidence=0.63,
                        source_refs=["document:7/page:2"],
                        relevance_score=0.63,
                    )
                ],
            )
        )
        retriever = BrainKnowledgeRetriever(light_rag_retriever=light_rag, semantic_retriever=semantic)

        result = asyncio.run(
            retriever.retrieve(
                "Aspire",
                tenant_id="tenant-a",
                correlation_id="corr-light-empty",
                limit=3,
            )
        )

        self.assertEqual("Semantic fallback result", result.results[0].content)
        self.assertEqual(1, len(light_rag.calls))
        self.assertEqual(1, len(semantic.calls))
        self.assertEqual("corr-light-empty", semantic.calls[0]["correlation_id"])

    def test_brain_knowledge_retriever_falls_back_to_semantic_when_lightrag_raises(self):
        light_rag = CapturingRetriever(error=RuntimeError("LightRAG unavailable"))
        semantic = CapturingRetriever(
            KnowledgeResult(
                tenant_id="tenant-a",
                correlation_id="corr-fallback",
                results=[
                    KnowledgeItem(
                        content="Semantic fallback result",
                        confidence=0.55,
                        source_refs=["document:8/page:1"],
                        relevance_score=0.55,
                    )
                ],
            )
        )
        retriever = BrainKnowledgeRetriever(light_rag_retriever=light_rag, semantic_retriever=semantic)

        result = asyncio.run(
            retriever.retrieve(
                "Aspire",
                tenant_id="tenant-a",
                correlation_id="corr-fallback",
                limit=2,
            )
        )

        self.assertEqual("Semantic fallback result", result.results[0].content)
        self.assertEqual(1, len(semantic.calls))


if __name__ == "__main__":
    unittest.main()
