from __future__ import annotations

import asyncio
import sys
import unittest
from pathlib import Path
from unittest.mock import MagicMock

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.models.models import SemanticQuery
from app.routers import rag
from app.services.neo4j_service import Neo4jService


class FakeNeo4jService:
    def __init__(self, results: list[dict]):
        self.results = results
        self.calls: list[tuple[str, int]] = []

    def search_similar_content(self, query: str, limit: int = 10):
        self.calls.append((query, limit))
        return list(self.results)


class RagSemanticSearchTests(unittest.TestCase):
    def test_semantic_search_preserves_scored_results_after_filtering(self):
        neo4j = FakeNeo4jService(
            [
                {
                    "content": "Aspire AppHost coordinates the web and API projects.",
                    "page_number": 2,
                    "filename": "guide.pdf",
                    "document_id": 7,
                    "source_confidence": 0.91,
                    "confidence": 0.91,
                    "relevance_score": 0.91,
                },
                {
                    "content": "Other document content.",
                    "page_number": 1,
                    "filename": "other.pdf",
                    "document_id": 8,
                    "source_confidence": 0.42,
                    "confidence": 0.42,
                    "relevance_score": 0.42,
                },
            ]
        )

        response = asyncio.run(
            rag.semantic_search(
                SemanticQuery(query="Aspire", document_ids=[7], limit=3),
                neo4j=neo4j,
            )
        )

        self.assertEqual(("Aspire", 3), neo4j.calls[0])
        self.assertEqual(1, response["count"])
        self.assertEqual(1, len(response["results"]))
        self.assertEqual(0.91, response["results"][0]["confidence"])
        self.assertEqual(0.91, response["results"][0]["relevance_score"])
        self.assertEqual(0.91, response["results"][0]["source_confidence"])

    def test_search_similar_content_projects_confidence_fields(self):
        service = Neo4jService(uri="bolt://test", user="neo4j", password="secret")
        session = MagicMock()
        session.run.return_value = [
            {
                "content": "Matched content",
                "page_number": 4,
                "filename": "match.pdf",
                "document_id": 11,
                "source_confidence": 0.88,
                "confidence": 0.88,
                "relevance_score": 0.88,
            }
        ]

        driver = MagicMock()
        driver.session.return_value.__enter__.return_value = session
        driver.session.return_value.__exit__.return_value = False
        service._driver = driver

        results = service.search_similar_content("matched", limit=2)

        query_text = session.run.call_args.args[0]
        query_params = session.run.call_args.args[1]

        self.assertEqual("matched", query_params["query"])
        self.assertEqual(2, query_params["limit"])
        self.assertIn("d.source_confidence as source_confidence", query_text)
        self.assertIn("coalesce(d.source_confidence, 0.5) as confidence", query_text)
        self.assertIn("coalesce(d.source_confidence, 0.5) as relevance_score", query_text)
        self.assertEqual(0.88, results[0]["confidence"])
        self.assertEqual(0.88, results[0]["relevance_score"])


if __name__ == "__main__":
    unittest.main()
