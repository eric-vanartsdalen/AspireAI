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
        self.vector_calls = []

    def search_similar_content(self, query, limit=10):
        self.calls.append({"query": query, "limit": limit})
        return list(self.results)

    def search_claims(self, query, limit=10):
        """Search claims - returns empty by default for backward compatibility"""
        self.calls.append({"query": query, "limit": limit})
        return getattr(self, 'search_claims_results', [])

    def search_claims_vector(self, query_embedding, limit=10, similarity_threshold=0.7):
        self.vector_calls.append({"kind": "claim", "limit": limit, "threshold": similarity_threshold})
        return getattr(self, "search_claims_vector_results", [])

    def search_pages_vector(self, query_embedding, limit=10, similarity_threshold=0.7):
        self.vector_calls.append({"kind": "page", "limit": limit, "threshold": similarity_threshold})
        return getattr(self, "search_pages_vector_results", [])


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


class _FakeEmbeddingService:
    """Lightweight stand-in for ``EmbeddingService`` used in vector-search tests."""

    def __init__(self, embedding: list[float] | None = None, error: Exception | None = None):
        self._embedding = embedding
        self._error = error

    def is_available(self) -> bool:
        return self._error is None

    def embed_text(self, text: str) -> list[float]:
        if self._error is not None:
            raise self._error
        return self._embedding or []


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

    def test_lightrag_retriever_fails_closed_on_unscored_response_text(self):
        """When response text has no score and no enrichment is possible, fail closed"""
        payload = {"data": {"response": "Fallback response."}}
        retriever = LightRagRetriever(query_service=FakeLightRagQueryService(payload))

        result = asyncio.run(retriever.retrieve("what is policy", limit=3))

        # Should return empty when confidence cannot be resolved
        self.assertEqual(0, len(result.results))

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
        neo4j.search_claims_results = []  # No claims, will use page results
        retriever = SemanticKnowledgeRetriever(neo4j)

        result = asyncio.run(
            retriever.retrieve(
                "Aspire",
                tenant_id="tenant-a",
                correlation_id="corr-semantic",
                limit=3,
            )
        )

        self.assertEqual("tenant-a", result.tenant_id)
        self.assertEqual("corr-semantic", result.correlation_id)
        self.assertEqual(1, len(result.results))
        self.assertEqual(0.63, result.results[0].confidence)
        self.assertEqual(0.63, result.results[0].relevance_score)
        self.assertEqual(
            ["document:7/page:2", "file:guide.pdf"],
            result.results[0].source_refs,
        )

    def test_semantic_knowledge_retriever_falls_back_to_text_when_vector_results_are_empty(self):
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
        neo4j.search_claims_results = []
        neo4j.search_claims_vector_results = []
        neo4j.search_pages_vector_results = []
        retriever = SemanticKnowledgeRetriever(
            neo4j,
            embedding_service=_FakeEmbeddingService(embedding=[0.1, 0.2, 0.3]),
        )

        result = asyncio.run(
            retriever.retrieve(
                "Aspire",
                tenant_id="tenant-a",
                correlation_id="corr-semantic-fallback",
                limit=3,
            )
        )

        self.assertEqual("tenant-a", result.tenant_id)
        self.assertEqual("corr-semantic-fallback", result.correlation_id)
        self.assertEqual(1, len(result.results))
        self.assertEqual("Aspire AppHost coordinates the web and API projects.", result.results[0].content)
        self.assertEqual(
            [{"kind": "claim", "limit": 3, "threshold": 0.7}, {"kind": "page", "limit": 3, "threshold": 0.7}],
            neo4j.vector_calls,
        )
        self.assertEqual(
            [{"query": "Aspire", "limit": 3}, {"query": "Aspire", "limit": 3}],
            neo4j.calls,
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
        self.assertEqual(1, len(semantic.calls))

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

    def test_brain_knowledge_retriever_supplements_single_document_lightrag_results_with_semantic_hits(self):
        light_rag = CapturingRetriever(
            KnowledgeResult(
                tenant_id="tenant-a",
                correlation_id="corr-merge",
                results=[
                    KnowledgeItem(
                        content="LightRAG primary result",
                        confidence=0.72,
                        source_refs=["document:1/page:1"],
                        relevance_score=0.72,
                    ),
                    KnowledgeItem(
                        content="LightRAG secondary chunk",
                        confidence=0.69,
                        source_refs=["document:1/page:2"],
                        relevance_score=0.69,
                    ),
                ],
            )
        )
        semantic = CapturingRetriever(
            KnowledgeResult(
                tenant_id="tenant-a",
                correlation_id="corr-merge",
                results=[
                    KnowledgeItem(
                        content="Semantic YouTube transcript hit",
                        confidence=0.91,
                        source_refs=["document:3/page:1"],
                        relevance_score=0.91,
                    )
                ],
            )
        )
        retriever = BrainKnowledgeRetriever(light_rag_retriever=light_rag, semantic_retriever=semantic)

        result = asyncio.run(
            retriever.retrieve(
                "what did Jeff Fritz say about Squad",
                tenant_id="tenant-a",
                correlation_id="corr-merge",
                limit=3,
            )
        )

        self.assertEqual(1, len(light_rag.calls))
        self.assertEqual(1, len(semantic.calls))
        self.assertEqual(
            [
                "LightRAG primary result",
                "LightRAG secondary chunk",
                "Semantic YouTube transcript hit",
            ],
            [item.content for item in result.results],
        )

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

    def test_semantic_retriever_uses_real_source_confidence_from_neo4j(self):
        """P2-B: Semantic fallback should use real confidence from Neo4j, not DEFAULT_CONFIDENCE=0.5"""
        neo4j = FakeNeo4jService(
            [
                {
                    "content": "Textbook excerpt with high confidence.",
                    "document_id": 12,
                    "page_number": 5,
                    "filename": "textbook.pdf",
                    "source_confidence": 0.9,
                    "confidence": 0.9,
                    "relevance_score": 0.9,
                }
            ]
        )
        neo4j.search_claims_results = []  # No claims, use page results with high confidence
        retriever = SemanticKnowledgeRetriever(neo4j)

        result = asyncio.run(retriever.retrieve("textbook", limit=5))

        self.assertEqual(1, len(result.results))
        item = result.results[0]
        self.assertEqual("Textbook excerpt with high confidence.", item.content)
        self.assertEqual(0.9, item.confidence, "Should use Neo4j confidence, not DEFAULT 0.5")
        self.assertEqual(0.9, item.relevance_score)
        self.assertIn("document:12/page:5", item.source_refs)
        self.assertIn("file:textbook.pdf", item.source_refs)

    def test_semantic_retriever_queries_claims_before_pages(self):
        """P2-B: Semantic retrieval should query Claim nodes first, then fall back to Page nodes"""
        neo4j = FakeNeo4jService([])
        # Mock both search methods
        neo4j.search_claims_results = [
            {
                "content": "Aspire orchestrates services declaratively.",
                "confidence": 0.85,
                "relevance_score": 0.85,
                "document_id": 3,
                "page_number": 1,
                "filename": "guide.pdf",
                "result_type": "claim"
            }
        ]
        neo4j.search_page_results = [
            {
                "content": "Page-level fallback content.",
                "confidence": 0.6,
                "document_id": 3,
                "page_number": 1,
            }
        ]
        
        # Update fake to return claims first
        def search_claims(query, limit):
            neo4j.calls.append({"method": "search_claims", "query": query, "limit": limit})
            return neo4j.search_claims_results
        
        def search_similar_content(query, limit):
            neo4j.calls.append({"method": "search_similar_content", "query": query, "limit": limit})
            return neo4j.search_page_results
        
        neo4j.search_claims = search_claims
        neo4j.search_similar_content = search_similar_content
        
        retriever = SemanticKnowledgeRetriever(neo4j)
        result = asyncio.run(retriever.retrieve("Aspire", limit=3))
        
        # Should query claims first
        self.assertEqual("search_claims", neo4j.calls[0]["method"])
        # Should NOT query pages because claims returned results
        self.assertEqual(1, len(neo4j.calls), "Should only call search_claims when claims exist")
        
        # Should return claim-based results with higher confidence
        self.assertEqual(1, len(result.results))
        self.assertEqual("Aspire orchestrates services declaratively.", result.results[0].content)
        self.assertEqual(0.85, result.results[0].confidence)

    def test_semantic_retriever_falls_back_to_pages_when_no_claims(self):
        """P2-B: Semantic retrieval should fall back to pages when no claims found"""
        neo4j = FakeNeo4jService([])
        neo4j.search_claims_results = []  # No claims
        neo4j.search_page_results = [
            {
                "content": "Page-level fallback content.",
                "confidence": 0.6,
                "document_id": 4,
                "page_number": 2,
                "filename": "fallback.pdf",
            }
        ]
        
        def search_claims(query, limit):
            neo4j.calls.append({"method": "search_claims", "query": query, "limit": limit})
            return neo4j.search_claims_results
        
        def search_similar_content(query, limit):
            neo4j.calls.append({"method": "search_similar_content", "query": query, "limit": limit})
            return neo4j.search_page_results
        
        neo4j.search_claims = search_claims
        neo4j.search_similar_content = search_similar_content
        
        retriever = SemanticKnowledgeRetriever(neo4j)
        result = asyncio.run(retriever.retrieve("query", limit=3))
        
        # Should try claims first, then fall back to pages
        self.assertEqual(2, len(neo4j.calls))
        self.assertEqual("search_claims", neo4j.calls[0]["method"])
        self.assertEqual("search_similar_content", neo4j.calls[1]["method"])
        
        # Should return page-based results
        self.assertEqual(1, len(result.results))
        self.assertEqual("Page-level fallback content.", result.results[0].content)
        self.assertEqual(0.6, result.results[0].confidence)

    def test_semantic_retriever_falls_back_to_pages_when_claims_are_out_of_scope(self):
        """Scoped retrieval should still fall back when claim hits are for other documents."""
        neo4j = FakeNeo4jService([])
        neo4j.search_claims_results = [
            {
                "content": "Out-of-scope claim result.",
                "confidence": 0.82,
                "document_id": 999,
                "page_number": 1,
                "filename": "other.pdf",
                "result_type": "claim",
            }
        ]
        neo4j.search_page_results = [
            {
                "content": "Scoped page fallback content.",
                "confidence": 0.67,
                "document_id": 7,
                "page_number": 4,
                "filename": "scoped.pdf",
            }
        ]

        def search_claims(query, limit):
            neo4j.calls.append({"method": "search_claims", "query": query, "limit": limit})
            return neo4j.search_claims_results

        def search_similar_content(query, limit):
            neo4j.calls.append({"method": "search_similar_content", "query": query, "limit": limit})
            return neo4j.search_page_results

        neo4j.search_claims = search_claims
        neo4j.search_similar_content = search_similar_content

        retriever = SemanticKnowledgeRetriever(neo4j)
        result = asyncio.run(retriever.retrieve("Aspire", limit=3, document_ids=[7]))

        self.assertEqual(2, len(neo4j.calls))
        self.assertEqual("search_claims", neo4j.calls[0]["method"])
        self.assertEqual("search_similar_content", neo4j.calls[1]["method"])
        self.assertEqual(1, len(result.results))
        self.assertEqual("Scoped page fallback content.", result.results[0].content)
        self.assertEqual(0.67, result.results[0].confidence)
        self.assertEqual(["document:7/page:4", "file:scoped.pdf"], result.results[0].source_refs)

    # ------------------------------------------------------------------
    # P2-C: Vector search wiring
    # ------------------------------------------------------------------

    def test_semantic_retriever_uses_vector_search_when_embedding_available(self):
        """P2-C: When EmbeddingService is provided and working, vector search is used."""
        neo4j = FakeNeo4jService([])

        vector_claim_results = [
            {
                "content": "Vector-matched claim.",
                "confidence": 0.92,
                "relevance_score": 0.92,
                "document_id": 10,
                "page_number": 3,
                "filename": "vector.pdf",
                "result_type": "claim",
            }
        ]

        def search_claims_vector(query_embedding, limit, threshold=0.7):
            neo4j.calls.append({"method": "search_claims_vector", "limit": limit})
            return vector_claim_results

        def search_pages_vector(query_embedding, limit, threshold=0.7):
            neo4j.calls.append({"method": "search_pages_vector", "limit": limit})
            return []

        neo4j.search_claims_vector = search_claims_vector
        neo4j.search_pages_vector = search_pages_vector

        fake_embedding = _FakeEmbeddingService(embedding=[0.1, 0.2, 0.3])
        retriever = SemanticKnowledgeRetriever(neo4j, embedding_service=fake_embedding)
        result = asyncio.run(retriever.retrieve("vector query", limit=5))

        self.assertEqual(1, len(result.results))
        self.assertEqual("Vector-matched claim.", result.results[0].content)
        self.assertEqual(0.92, result.results[0].confidence)
        # Should have used vector search, not text search
        methods = [c["method"] for c in neo4j.calls if "method" in c]
        self.assertIn("search_claims_vector", methods)
        self.assertNotIn("search_claims", methods)
        self.assertNotIn("search_similar_content", methods)

    def test_semantic_retriever_vector_falls_back_to_pages_vector(self):
        """P2-C: When vector claim search returns empty, falls back to vector page search."""
        neo4j = FakeNeo4jService([])

        vector_page_results = [
            {
                "content": "Vector page match.",
                "confidence": 0.78,
                "document_id": 11,
                "page_number": 1,
                "filename": "pages.pdf",
            }
        ]

        def search_claims_vector(query_embedding, limit, threshold=0.7):
            neo4j.calls.append({"method": "search_claims_vector", "limit": limit})
            return []

        def search_pages_vector(query_embedding, limit, threshold=0.7):
            neo4j.calls.append({"method": "search_pages_vector", "limit": limit})
            return vector_page_results

        neo4j.search_claims_vector = search_claims_vector
        neo4j.search_pages_vector = search_pages_vector

        fake_embedding = _FakeEmbeddingService(embedding=[0.4, 0.5, 0.6])
        retriever = SemanticKnowledgeRetriever(neo4j, embedding_service=fake_embedding)
        result = asyncio.run(retriever.retrieve("page query", limit=5))

        methods = [c["method"] for c in neo4j.calls if "method" in c]
        self.assertEqual(["search_claims_vector", "search_pages_vector"], methods)
        self.assertEqual(1, len(result.results))
        self.assertEqual("Vector page match.", result.results[0].content)

    def test_semantic_retriever_falls_back_to_text_when_embedding_unavailable(self):
        """P2-C: When EmbeddingService raises, retriever falls back to text search."""
        neo4j = FakeNeo4jService([])
        neo4j.search_claims_results = [
            {
                "content": "Text claim fallback.",
                "confidence": 0.70,
                "document_id": 5,
                "page_number": 1,
                "filename": "text.pdf",
                "result_type": "claim",
            }
        ]

        fake_embedding = _FakeEmbeddingService(error=RuntimeError("Ollama offline"))
        retriever = SemanticKnowledgeRetriever(neo4j, embedding_service=fake_embedding)
        result = asyncio.run(retriever.retrieve("fallback query", limit=3))

        self.assertEqual(1, len(result.results))
        self.assertEqual("Text claim fallback.", result.results[0].content)
        # Should have used text search
        call_kinds = [c.get("method") for c in neo4j.calls]
        self.assertNotIn("search_claims_vector", call_kinds)

    def test_semantic_retriever_falls_back_to_text_when_no_embedding_service(self):
        """P2-C: Without an EmbeddingService, retriever uses text search (backward compat)."""
        neo4j = FakeNeo4jService(
            [
                {
                    "content": "Text-only result.",
                    "document_id": 1,
                    "page_number": 1,
                    "filename": "compat.pdf",
                    "score": 0.55,
                }
            ]
        )
        neo4j.search_claims_results = []
        retriever = SemanticKnowledgeRetriever(neo4j)  # No embedding service

        result = asyncio.run(retriever.retrieve("backwards compat", limit=5))

        self.assertEqual(1, len(result.results))
        self.assertEqual("Text-only result.", result.results[0].content)

    def test_brain_retriever_passes_embedding_service_through(self):
        """P2-C: BrainKnowledgeRetriever forwards embedding_service to SemanticKnowledgeRetriever."""
        neo4j = FakeNeo4jService([])

        vector_claim_results = [
            {
                "content": "Brain vector claim.",
                "confidence": 0.88,
                "document_id": 20,
                "page_number": 1,
                "filename": "brain.pdf",
                "result_type": "claim",
            }
        ]

        def search_claims_vector(query_embedding, limit, threshold=0.7):
            neo4j.calls.append({"method": "search_claims_vector", "limit": limit})
            return vector_claim_results

        def search_pages_vector(query_embedding, limit, threshold=0.7):
            neo4j.calls.append({"method": "search_pages_vector", "limit": limit})
            return []

        neo4j.search_claims_vector = search_claims_vector
        neo4j.search_pages_vector = search_pages_vector

        fake_embedding = _FakeEmbeddingService(embedding=[0.7, 0.8, 0.9])

        # LightRAG returns empty → triggers semantic fallback
        light_rag = CapturingRetriever(
            KnowledgeResult(tenant_id="t", correlation_id="c", results=[])
        )
        retriever = BrainKnowledgeRetriever(
            light_rag_retriever=light_rag,
            neo4j_service=neo4j,
            embedding_service=fake_embedding,
        )

        result = asyncio.run(retriever.retrieve("brain vector", limit=3))

        self.assertEqual(1, len(result.results))
        self.assertEqual("Brain vector claim.", result.results[0].content)
        methods = [c["method"] for c in neo4j.calls if "method" in c]
        self.assertIn("search_claims_vector", methods)


if __name__ == "__main__":
    unittest.main()
