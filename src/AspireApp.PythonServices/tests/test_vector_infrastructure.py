"""
Tests for P2-C vector index infrastructure.

Validates:
- Vector index creation (idempotent, safe to run on startup)
- Vector search methods (ready for use when embeddings are populated)
- Embedding service availability through Aspire-wired Ollama config or local fallback
"""

import json
import os
import sys
from pathlib import Path

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent))

import unittest
from unittest.mock import Mock, MagicMock, patch
from app.services.neo4j_service import Neo4jService
from app.services.embedding_service import EmbeddingService


class FakeHttpResponse:
    def __init__(self, payload):
        self.payload = json.dumps(payload).encode("utf-8")

    def read(self):
        return self.payload

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


class FakeNeo4jDriver:
    """Fake Neo4j driver for testing index creation without live database."""
    
    def __init__(self):
        self.executed_queries = []
        self.session_mock = MagicMock()
    
    def session(self):
        return self
    
    def __enter__(self):
        return self.session_mock
    
    def __exit__(self, *args):
        pass
    
    def run(self, query, parameters=None):
        self.executed_queries.append({
            "query": query,
            "parameters": parameters or {}
        })
        return MagicMock()
    
    def close(self):
        pass


class TestVectorIndexCreation(unittest.TestCase):
    """Test that vector indexes are created correctly during service initialization."""
    
    def test_ensure_vector_indexes_creates_page_and_claim_indexes(self):
        """Vector indexes should be created for both Page and Claim nodes."""
        fake_driver = FakeNeo4jDriver()

        with patch.dict(os.environ, {"EMBEDDING_DIM": "1024"}, clear=False):
            service = Neo4jService()
            service._driver = fake_driver

            service._ensure_vector_indexes(fake_driver.session_mock)

            executed = fake_driver.session_mock.run.call_args_list
            self.assertGreaterEqual(len(executed), 2, "Should create at least 2 vector indexes")

            queries = [call[0][0] for call in executed]

            page_index_query = next((q for q in queries if "page_content_vector" in q), None)
            self.assertIsNotNone(page_index_query, "page_content_vector index should be created")
            self.assertIn("Page", page_index_query)
            self.assertIn("content_embedding", page_index_query)
            self.assertIn("1024", page_index_query)

            claim_index_query = next((q for q in queries if "claim_text_vector" in q), None)
            self.assertIsNotNone(claim_index_query, "claim_text_vector index should be created")
            self.assertIn("Claim", claim_index_query)
            self.assertIn("text_embedding", claim_index_query)
            self.assertIn("1024", claim_index_query)
    
    def test_vector_index_creation_is_idempotent(self):
        """Vector index creation should use IF NOT EXISTS for safety."""
        fake_driver = FakeNeo4jDriver()
        service = Neo4jService()
        service._driver = fake_driver
        
        # Call twice - should not raise errors
        service._ensure_vector_indexes(fake_driver.session_mock)
        service._ensure_vector_indexes(fake_driver.session_mock)
        
        # Both calls should succeed (idempotent)
        executed = fake_driver.session_mock.run.call_args_list
        self.assertGreaterEqual(len(executed), 4, "Should handle multiple calls")


class TestVectorSearchMethods(unittest.TestCase):
    """Test vector search methods are ready to use when embeddings are populated."""
    
    def test_search_claims_vector_executes_correct_query(self):
        """Vector search should query claim_text_vector index with embedding."""
        service = Neo4jService()
        fake_session = MagicMock()
        fake_result = MagicMock()
        fake_result.__iter__ = Mock(return_value=iter([]))
        fake_session.run.return_value = fake_result
        
        # Mock driver to return our fake session
        with patch.object(service, 'get_driver') as mock_driver:
            mock_driver.return_value.session.return_value.__enter__ = Mock(return_value=fake_session)
            mock_driver.return_value.session.return_value.__exit__ = Mock(return_value=False)
            
            # Execute vector search with sample embedding
            query_embedding = [0.1] * 384  # 384-dimensional vector
            results = service.search_claims_vector(query_embedding, limit=5)
            
            # Verify query executed with vector index call
            self.assertEqual(1, fake_session.run.call_count)
            call_args = fake_session.run.call_args
            # Get query from positional args
            query = call_args.args[0] if call_args.args else ""
            # Get params from either positional args or kwargs
            params = call_args.args[1] if len(call_args.args) > 1 else call_args.kwargs
            
            # Verify uses vector index
            self.assertIn("db.index.vector.queryNodes", query)
            self.assertIn("claim_text_vector", query)
            
            # Verify embedding passed correctly
            self.assertIn("query_embedding", params)
            self.assertEqual(query_embedding, params["query_embedding"])
            self.assertEqual(5, params["limit"])
    
    def test_search_pages_vector_executes_correct_query(self):
        """Page vector search should query page_content_vector index."""
        service = Neo4jService()
        fake_session = MagicMock()
        fake_result = MagicMock()
        fake_result.__iter__ = Mock(return_value=iter([]))
        fake_session.run.return_value = fake_result
        
        with patch.object(service, 'get_driver') as mock_driver:
            mock_driver.return_value.session.return_value.__enter__ = Mock(return_value=fake_session)
            mock_driver.return_value.session.return_value.__exit__ = Mock(return_value=False)
            
            query_embedding = [0.2] * 384
            results = service.search_pages_vector(query_embedding, limit=10, similarity_threshold=0.8)
            
            self.assertEqual(1, fake_session.run.call_count)
            call_args = fake_session.run.call_args
            query = call_args.args[0] if call_args.args else ""
            params = call_args.args[1] if len(call_args.args) > 1 else call_args.kwargs
            
            self.assertIn("db.index.vector.queryNodes", query)
            self.assertIn("page_content_vector", query)
            self.assertIn("query_embedding", params)
            self.assertEqual(query_embedding, params["query_embedding"])
            self.assertEqual(0.8, params["similarity_threshold"])


class TestEmbeddingService(unittest.TestCase):
    """Test embedding service foundation against Aspire and local configurations."""

    def test_embedding_service_initializes_with_defaults(self):
        """Service should initialize with local fallback defaults when no env is set."""
        with patch.dict(os.environ, {}, clear=True):
            service = EmbeddingService()

            self.assertIsNotNone(service.model_name)
            self.assertIn("MiniLM", service.model_name)
            self.assertEqual(384, service.get_embedding_dimension())
            self.assertEqual("", service.endpoint)

    def test_embedding_service_uses_aspire_ollama_configuration(self):
        """AppHost-provided Ollama settings should drive runtime configuration."""
        with patch.dict(
            os.environ,
            {
                "OLLAMA_ENDPOINT": "http://ollama:11434",
                "EMBEDDING_MODEL": "bge-m3:latest",
                "EMBEDDING_DIM": "1024",
            },
            clear=False,
        ):
            service = EmbeddingService()

            self.assertEqual("http://ollama:11434", service.endpoint)
            self.assertEqual("bge-m3:latest", service.model_name)
            self.assertEqual(1024, service.get_embedding_dimension())
            self.assertTrue(service.is_available())

    def test_embedding_service_calls_ollama_embed_endpoint_for_single_text(self):
        """Single-text embeddings should use the Ollama endpoint when configured."""
        with patch.dict(
            os.environ,
            {
                "OLLAMA_ENDPOINT": "http://ollama:11434",
                "EMBEDDING_MODEL": "bge-m3:latest",
                "EMBEDDING_DIM": "1024",
            },
            clear=False,
        ):
            service = EmbeddingService()

            with patch("app.services.embedding_service.request.urlopen") as mock_urlopen:
                mock_urlopen.return_value = FakeHttpResponse({"embedding": [0.5, 0.6]})

                result = service.embed_text("test query")

                self.assertEqual([0.5, 0.6], result)
                request_obj = mock_urlopen.call_args.args[0]
                self.assertTrue(request_obj.full_url.endswith("/api/embed"))

    def test_embedding_service_batch_calls_ollama_embed_endpoint(self):
        """Batch embeddings should use Ollama's batched endpoint when configured."""
        with patch.dict(
            os.environ,
            {
                "OLLAMA_ENDPOINT": "http://ollama:11434",
                "EMBEDDING_MODEL": "bge-m3:latest",
                "EMBEDDING_DIM": "1024",
            },
            clear=False,
        ):
            service = EmbeddingService()

            with patch("app.services.embedding_service.request.urlopen") as mock_urlopen:
                mock_urlopen.return_value = FakeHttpResponse(
                    {"embeddings": [[0.1, 0.2], [0.3, 0.4]]}
                )

                result = service.embed_batch(["query 1", "query 2"])

                self.assertEqual([[0.1, 0.2], [0.3, 0.4]], result)
                request_obj = mock_urlopen.call_args.args[0]
                self.assertTrue(request_obj.full_url.endswith("/api/embed"))

    def test_embedding_service_handles_missing_local_model_gracefully(self):
        """Local fallback should not crash when sentence-transformers is unavailable."""
        with patch.dict(os.environ, {}, clear=True):
            service = EmbeddingService()

            with patch.object(service, "_load_model", return_value=None):
                result = service.embed_text("test query")
                self.assertIsNone(result)
                self.assertFalse(service.is_available())

    def test_embedding_service_generates_local_embeddings_when_available(self):
        """Local fallback should still work for direct Python runs."""
        with patch.dict(os.environ, {}, clear=True):
            service = EmbeddingService()

            mock_model = MagicMock()
            mock_embedding = MagicMock()
            mock_embedding.tolist.return_value = [0.5] * 384
            mock_model.encode.return_value = mock_embedding

            with patch.object(service, "_load_model", return_value=mock_model):
                result = service.embed_text("test query")

                self.assertIsNotNone(result)
                self.assertEqual(384, len(result))
                self.assertIsInstance(result, list)
                self.assertTrue(all(isinstance(x, float) for x in result))

    def test_embedding_service_batch_uses_local_model_when_ollama_not_configured(self):
        """Local fallback batch encoding should still honor batch size."""
        with patch.dict(os.environ, {}, clear=True):
            service = EmbeddingService()

            mock_model = MagicMock()
            embedding1 = MagicMock()
            embedding1.tolist.return_value = [0.1] * 384
            embedding2 = MagicMock()
            embedding2.tolist.return_value = [0.2] * 384
            embedding3 = MagicMock()
            embedding3.tolist.return_value = [0.3] * 384
            mock_model.encode.return_value = [embedding1, embedding2, embedding3]

            with patch.object(service, "_load_model", return_value=mock_model):
                texts = ["query 1", "query 2", "query 3"]
                results = service.embed_batch(texts, batch_size=32)

                self.assertIsNotNone(results)
                self.assertEqual(3, len(results))

                mock_model.encode.assert_called_once()
                call_args = mock_model.encode.call_args
                self.assertEqual(texts, call_args[0][0])
                self.assertEqual(32, call_args[1]["batch_size"])


if __name__ == "__main__":
    unittest.main()
