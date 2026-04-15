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

from app.brain.knowledge import LightRAGRetriever, LightRagRetriever
from app.contracts import BrainQueryRequest, IKnowledgeRetriever, KnowledgeItem, KnowledgeResult
from app.models.models import LightRagQueryRequest
from app.routers import rag


class FakeLightRagQueryService:
    def __init__(self, payload: dict):
        self.payload = payload
        self.requests: list[LightRagQueryRequest] = []

    def query_data(self, query_request: LightRagQueryRequest) -> dict:
        self.requests.append(query_request)
        return self.payload


class FakeNeo4jService:
    def __init__(self, results: list[dict]):
        self.results = results
        self.calls: list[dict] = []
        self.confidence_lookups: list[dict] = []

    def search_similar_content(self, query: str, limit: int) -> list[dict]:
        self.calls.append({"query": query, "limit": limit})
        return self.results
    
    def get_confidence_by_provenance(
        self, 
        document_id: int, 
        page_number: int | None = None
    ) -> float | None:
        """Mock confidence lookup for testing enrichment"""
        self.confidence_lookups.append({
            "document_id": document_id, 
            "page_number": page_number
        })
        # Return mocked confidence based on test setup
        return getattr(self, '_mock_confidence', None)


class CapturingRetriever(IKnowledgeRetriever):
    def __init__(self):
        self.calls: list[dict] = []

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
        return KnowledgeResult(
            tenant_id=tenant_id,
            correlation_id=correlation_id or "generated-correlation",
            results=[
                KnowledgeItem(
                    content="Retrieved through seam",
                    confidence=0.8,
                    source_refs=["document:1/page:1"],
                    relevance_score=0.8,
                )
            ],
        )


class LightRagRetrieverTests(unittest.TestCase):
    def test_contract_name_alias_resolves_to_retriever(self):
        self.assertTrue(issubclass(LightRAGRetriever, LightRagRetriever))

    def test_retrieve_maps_scored_chunk_results_to_knowledge_contract(self):
        query_service = FakeLightRagQueryService(
            {
                "status": "success",
                "data": {
                    "results": [
                        {
                            "chunk_content": "Aspire keeps orchestration explicit.",
                            "metadata": {
                                "confidence": 0.77,
                                "references": ["document:4/page:1"],
                            },
                        }
                    ]
                },
            }
        )
        retriever = LightRagRetriever(query_service)

        result = asyncio.run(
            retriever.retrieve(
                "Aspire",
                tenant_id="tenant-a",
                correlation_id="corr-1",
                limit=5,
                mode="mix",
                top_k=5,
                chunk_top_k=3,
                include_references=True,
                include_chunk_content=True,
            )
        )

        self.assertEqual("tenant-a", result.tenant_id)
        self.assertEqual("corr-1", result.correlation_id)
        self.assertEqual(1, len(result.results))
        self.assertEqual("Aspire keeps orchestration explicit.", result.results[0].content)
        self.assertEqual(0.77, result.results[0].confidence)
        self.assertEqual(["document:4/page:1"], result.results[0].source_refs)
        self.assertEqual(0.77, result.results[0].relevance_score)
        self.assertEqual("mix", query_service.requests[0].mode)
        self.assertEqual(5, query_service.requests[0].top_k)
        self.assertEqual(3, query_service.requests[0].chunk_top_k)

    def test_retrieve_generates_source_refs_from_document_fields(self):
        retriever = LightRagRetriever(
            FakeLightRagQueryService(
                {
                    "results": [
                        {
                            "content": "Page-level fact",
                            "score": "0.61",
                            "document_id": "7",
                            "page_number": 2,
                            "filename": "guide.pdf",
                        }
                    ]
                }
            )
        )

        result = asyncio.run(retriever.retrieve("fact", correlation_id="corr-2"))

        self.assertEqual(1, len(result.results))
        self.assertEqual(0.61, result.results[0].confidence)
        self.assertEqual(
            ["document:7/page:2", "file:guide.pdf"],
            result.results[0].source_refs,
        )

    def test_retrieve_uses_top_level_response_score_and_refs(self):
        retriever = LightRagRetriever(
            FakeLightRagQueryService(
                {
                    "response": "Aspire coordinates service startup.",
                    "references": ["document:9/page:3"],
                    "score": 0.83,
                }
            )
        )

        result = asyncio.run(retriever.retrieve("Aspire", tenant_id="tenant-b"))

        self.assertEqual("tenant-b", result.tenant_id)
        self.assertEqual(1, len(result.results))
        self.assertEqual("Aspire coordinates service startup.", result.results[0].content)
        self.assertEqual(0.83, result.results[0].confidence)
        self.assertEqual(["document:9/page:3"], result.results[0].source_refs)

    def test_retrieve_uses_nested_response_score_on_fallback_text(self):
        retriever = LightRagRetriever(
            FakeLightRagQueryService(
                {
                    "data": {
                        "response": "Nested LightRAG fallback response.",
                        "score": "0.41",
                        "references": ["document:3/page:4"],
                    }
                }
            )
        )

        result = asyncio.run(retriever.retrieve("fallback"))

        self.assertEqual(1, len(result.results))
        self.assertEqual("Nested LightRAG fallback response.", result.results[0].content)
        self.assertEqual(0.41, result.results[0].confidence)
        self.assertEqual(0.41, result.results[0].relevance_score)
        self.assertEqual(["document:3/page:4"], result.results[0].source_refs)

    def test_retrieve_maps_contexts_results_to_knowledge_contract(self):
        retriever = LightRagRetriever(
            FakeLightRagQueryService(
                {
                    "answer": "Aspire keeps orchestration explicit.",
                    "contexts": [
                        {
                            "content": "Aspire routes dependencies through the AppHost.",
                            "score": 0.73,
                            "source_doc": "/app/data/inputs/000004-guide.md",
                        }
                    ],
                }
            )
        )

        result = asyncio.run(retriever.retrieve("Aspire"))

        self.assertEqual(1, len(result.results))
        self.assertEqual("Aspire routes dependencies through the AppHost.", result.results[0].content)
        self.assertEqual(0.73, result.results[0].confidence)
        self.assertIn("file:000004-guide.md", result.results[0].source_refs)

    def test_retrieve_enriches_chunk_confidence_from_lightrag_file_path(self):
        neo4j = FakeNeo4jService(results=[])
        neo4j._mock_confidence = 0.84
        retriever = LightRagRetriever(
            FakeLightRagQueryService(
                {
                    "status": "success",
                    "message": "Query executed successfully",
                    "data": {
                        "chunks": [
                            {
                                "content": "Stored chunk content from LightRAG.",
                                "file_path": "/app/data/inputs/000007-guide.md",
                                "reference_id": "ref-1",
                            }
                        ],
                        "references": [
                            {
                                "reference_id": "ref-1",
                                "file_path": "/app/data/inputs/000007-guide.md",
                            }
                        ],
                    },
                }
            ),
            neo4j_service=neo4j,
        )

        result = asyncio.run(retriever.retrieve("guide"))

        self.assertEqual(1, len(result.results))
        self.assertEqual("Stored chunk content from LightRAG.", result.results[0].content)
        self.assertEqual(0.84, result.results[0].confidence)
        self.assertIn("document:7", result.results[0].source_refs)
        self.assertIn("file:000007-guide.md", result.results[0].source_refs)
        self.assertEqual([{"document_id": 7, "page_number": None}], neo4j.confidence_lookups)

    def test_retrieve_preserves_zero_confidence_scores(self):
        retriever = LightRagRetriever(
            FakeLightRagQueryService(
                {
                    "results": [
                        {
                            "content": "Low-confidence fact",
                            "confidence": 0.0,
                            "source_refs": ["document:11/page:5"],
                        }
                    ]
                }
            )
        )

        result = asyncio.run(retriever.retrieve("uncertain"))

        self.assertEqual(0.0, result.results[0].confidence)
        self.assertEqual(0.0, result.results[0].relevance_score)

    def test_lightrag_route_calls_retriever_seam_with_request_options(self):
        retriever = CapturingRetriever()
        request = LightRagQueryRequest(
            query="Aspire",
            mode="local",
            top_k=4,
            chunk_top_k=2,
            include_references=False,
            include_chunk_content=False,
            tenant_id="tenant-route",
            correlation_id="corr-route",
        )

        result = asyncio.run(rag.lightrag_query(request, retriever=retriever))

        self.assertEqual("tenant-route", result.tenant_id)
        self.assertEqual("corr-route", result.correlation_id)
        self.assertEqual(1, len(retriever.calls))
        self.assertEqual(4, retriever.calls[0]["limit"])
        self.assertEqual(
            {
                "mode": "local",
                "top_k": 4,
                "chunk_top_k": 2,
                "include_references": False,
                "include_chunk_content": False,
            },
            retriever.calls[0]["options"],
        )

    def test_semantic_search_preserves_downstream_score_fields(self):
        neo4j = FakeNeo4jService(
            [
                {
                    "content": "Semantic fallback result",
                    "document_id": 8,
                    "page_number": 3,
                    "filename": "semantic.pdf",
                    "score": 0.63,
                }
            ]
        )
        result = asyncio.run(rag.semantic_search(query=rag.SemanticQuery(query="fallback", limit=4), neo4j=neo4j))

        self.assertEqual("fallback", result["query"])
        self.assertEqual(1, result["count"])
        self.assertEqual(0.63, result["results"][0]["score"])
        self.assertEqual([{"query": "fallback", "limit": 4}], neo4j.calls)

    def test_query_route_calls_contract_retriever_with_brain_query_request(self):
        retriever = CapturingRetriever()
        request = BrainQueryRequest(
            query="Aspire",
            top_k=4,
            tenant_id="tenant-route",
            correlation_id="corr-route",
        )

        result = asyncio.run(rag.query_knowledge(request, retriever=retriever))

        self.assertEqual("tenant-route", result.tenant_id)
        self.assertEqual("corr-route", result.correlation_id)
        self.assertEqual(1, len(retriever.calls))
        self.assertEqual("Aspire", retriever.calls[0]["query"])
        self.assertEqual(4, retriever.calls[0]["limit"])
        self.assertEqual(
            {
                "top_k": 4,
                "chunk_top_k": 4,
                "include_references": True,
                "include_chunk_content": True,
            },
            retriever.calls[0]["options"],
        )

    def test_lightrag_retriever_enriches_unscored_result_with_neo4j_confidence(self):
        """When LightRAG omits score but provenance is available, enrich from Neo4j"""
        neo4j = FakeNeo4jService([])
        neo4j._mock_confidence = 0.82  # Mock stored confidence
        
        query_service = FakeLightRagQueryService(
            {
                "results": [
                    {
                        "content": "Unscored LightRAG result",
                        "document_id": 5,
                        "page_number": 3,
                        # No confidence/score field
                    }
                ]
            }
        )
        retriever = LightRagRetriever(query_service, neo4j_service=neo4j)
        
        result = asyncio.run(retriever.retrieve("query"))
        
        self.assertEqual(1, len(result.results))
        self.assertEqual(0.82, result.results[0].confidence)
        self.assertEqual(1, len(neo4j.confidence_lookups))
        self.assertEqual(5, neo4j.confidence_lookups[0]["document_id"])
        self.assertEqual(3, neo4j.confidence_lookups[0]["page_number"])

    def test_lightrag_retriever_enriches_from_parsed_source_refs(self):
        """When provenance is only in source_refs, parse and enrich from Neo4j"""
        neo4j = FakeNeo4jService([])
        neo4j._mock_confidence = 0.74
        
        query_service = FakeLightRagQueryService(
            {
                "results": [
                    {
                        "content": "Result with ref-only provenance",
                        "source_refs": ["document:12/page:7"],
                        # No document_id/page_number fields, no score
                    }
                ]
            }
        )
        retriever = LightRagRetriever(query_service, neo4j_service=neo4j)
        
        result = asyncio.run(retriever.retrieve("query"))
        
        self.assertEqual(1, len(result.results))
        self.assertEqual(0.74, result.results[0].confidence)
        self.assertEqual(1, len(neo4j.confidence_lookups))
        self.assertEqual(12, neo4j.confidence_lookups[0]["document_id"])
        self.assertEqual(7, neo4j.confidence_lookups[0]["page_number"])

    def test_lightrag_retriever_fails_closed_when_neo4j_returns_none(self):
        """When Neo4j cannot resolve confidence, fail closed (return empty) to force semantic fallback"""
        neo4j = FakeNeo4jService([])
        neo4j._mock_confidence = None  # Neo4j has no stored confidence
        
        query_service = FakeLightRagQueryService(
            {
                "results": [
                    {
                        "content": "Unresolved confidence result",
                        "document_id": 99,
                        "page_number": 1,
                        # No score, and Neo4j will return None
                    }
                ]
            }
        )
        retriever = LightRagRetriever(query_service, neo4j_service=neo4j)
        
        result = asyncio.run(retriever.retrieve("query"))
        
        # Should return empty results, forcing fallback to semantic retriever
        self.assertEqual(0, len(result.results))
        self.assertEqual(1, len(neo4j.confidence_lookups))

    def test_lightrag_retriever_without_neo4j_service_fails_closed(self):
        """When no Neo4j service is provided and no score exists, fail closed (return empty)"""
        query_service = FakeLightRagQueryService(
            {
                "results": [
                    {
                        "content": "Unscored result without Neo4j",
                        "document_id": 8,
                        "page_number": 2,
                        # No score, no Neo4j service
                    }
                ]
            }
        )
        retriever = LightRagRetriever(query_service, neo4j_service=None)
        
        result = asyncio.run(retriever.retrieve("query"))
        
        # Should return empty results, forcing fallback to semantic retriever
        self.assertEqual(0, len(result.results))

    def test_lightrag_retriever_preserves_explicit_scores_without_enrichment(self):
        """When LightRAG provides a score, use it directly without Neo4j lookup"""
        neo4j = FakeNeo4jService([])
        neo4j._mock_confidence = 0.99  # Would enrich if called, but shouldn't be
        
        query_service = FakeLightRagQueryService(
            {
                "results": [
                    {
                        "content": "Scored result",
                        "document_id": 6,
                        "page_number": 4,
                        "confidence": 0.68,  # Explicit score provided
                    }
                ]
            }
        )
        retriever = LightRagRetriever(query_service, neo4j_service=neo4j)
        
        result = asyncio.run(retriever.retrieve("query"))
        
        self.assertEqual(1, len(result.results))
        self.assertEqual(0.68, result.results[0].confidence)
        self.assertEqual(0, len(neo4j.confidence_lookups))  # No lookup needed


if __name__ == "__main__":
    unittest.main()
