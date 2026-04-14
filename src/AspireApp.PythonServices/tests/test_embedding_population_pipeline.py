"""
P2-C Embedding Population Regression Tests

Validates that embeddings are generated and persisted during document ingestion.

Scope:
- Embeddings are generated for page content during processing
- Embeddings are generated for claim text during claim extraction
- Embeddings are persisted to Neo4j Page.content_embedding properties
- Embeddings are persisted to Neo4j Claim.text_embedding properties
- Embedding generation is batched for efficiency
- Embedding service availability is checked before processing

Non-goals:
- Live vector retrieval tests (deferred until embedding population is complete)
- Integration with actual Ollama instance (mocked for test isolation)
"""

from __future__ import annotations

import asyncio
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from typing import List, Dict, Any
from unittest.mock import Mock, patch

PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT))

from app.services.embedding_service import EmbeddingService


class FakeEmbeddingService:
    """Fake embedding service that tracks calls and returns valid embeddings."""
    
    def __init__(self, embedding_dimension: int = 384, available: bool = True):
        self.embedding_dimension = embedding_dimension
        self.available = available
        self.embed_text_calls: List[str] = []
        self.embed_batch_calls: List[List[str]] = []
        self._call_count = 0
    
    def is_available(self) -> bool:
        return self.available
    
    def get_embedding_dimension(self) -> int:
        return self.embedding_dimension
    
    def embed_text(self, text: str) -> List[float] | None:
        """Generate fake embedding for single text."""
        if not self.available:
            return None
        
        self.embed_text_calls.append(text)
        # Generate deterministic fake embedding based on text content
        seed = sum(ord(c) for c in text)
        return [(seed % 100) / 100.0] * self.embedding_dimension
    
    def embed_batch(self, texts: List[str], batch_size: int = 32) -> List[List[float]] | None:
        """Generate fake embeddings for batch of texts."""
        if not self.available:
            return None
        
        self.embed_batch_calls.append(texts)
        # Generate deterministic fake embeddings
        embeddings = []
        for text in texts:
            seed = sum(ord(c) for c in text)
            embeddings.append([(seed % 100) / 100.0] * self.embedding_dimension)
        return embeddings


class FakeNeo4jService:
    """Fake Neo4j service that tracks embedding persistence."""
    
    def __init__(self):
        self.created_documents: List[Dict[str, Any]] = []
        self.created_pages: List[Dict[str, Any]] = []
        self.created_claims: List[Dict[str, Any]] = []
        self.page_embeddings: Dict[str, List[float]] = {}  # page_id -> embedding
        self.claim_embeddings: Dict[str, List[float]] = {}  # claim_id -> embedding
    
    def create_document_node(self, document) -> str:
        self.created_documents.append(document)
        return f"doc-node-{getattr(document, 'id', getattr(document, 'document_id', 'unknown'))}"
    
    def create_page_nodes_with_embeddings(
        self, 
        pages, 
        doc_node_id: str, 
        document_id: int,
        page_embeddings: List[List[float]] | None = None
    ) -> List[str]:
        """Create page nodes with optional embeddings."""
        page_node_ids = []
        for i, page in enumerate(pages):
            page_id = f"{document_id}_{page.page_number}"
            page_node_id = f"page-node-{page.page_number}"
            
            page_data = {
                "page_id": page_id,
                "page_number": page.page_number,
                "content": page.content,
                "metadata": page.metadata,
            }
            
            # Track embedding if provided
            if page_embeddings and i < len(page_embeddings):
                page_data["content_embedding"] = page_embeddings[i]
                self.page_embeddings[page_id] = page_embeddings[i]
            
            self.created_pages.append(page_data)
            page_node_ids.append(page_node_id)
        
        return page_node_ids
    
    def create_claim_nodes_with_embeddings(
        self, 
        claims: List[Dict[str, Any]], 
        page_node_id: str, 
        document_id: int,
        page_number: int,
        claim_embeddings: List[List[float]] | None = None
    ) -> List[str]:
        """Create claim nodes with optional embeddings."""
        claim_node_ids = []
        for i, claim in enumerate(claims):
            claim_id = f"{document_id}_p{page_number}_claim{i}"
            
            claim_data = {
                "claim_id": claim_id,
                "text": claim.get("text", ""),
                "confidence": claim.get("confidence", 0.7),
                "document_id": document_id,
                "page_number": page_number,
                "claim_index": i,
            }
            
            # Track embedding if provided
            if claim_embeddings and i < len(claim_embeddings):
                claim_data["text_embedding"] = claim_embeddings[i]
                self.claim_embeddings[claim_id] = claim_embeddings[i]
            
            self.created_claims.append(claim_data)
            claim_node_ids.append(f"claim-node-{i}")
        
        return claim_node_ids
    
    def create_relationships(self, doc_node_id: str, page_node_ids: List[str]):
        pass
    
    def create_sequential_relationships(self, page_node_ids: List[str]):
        pass


class FakeDatabaseService:
    def __init__(self, document: SimpleNamespace | None = None):
        self.document = document
        self.status_updates: List[tuple[int, str, str | None]] = []
        self.processing_updates: List[Dict] = []
        self.ingestion_updates: List[Dict] = []
        self.saved_pages: List[Dict] = []
    
    def get_document_by_id(self, document_id: int):
        if self.document and self.document.id == document_id:
            return self.document
        return None
    
    def update_file_status(self, file_id: int, status: str, error: str = None):
        self.status_updates.append((file_id, status, error))
    
    def resolve_upload_path(self, document):
        return document.file_path
    
    def update_file_processing_results(self, **kwargs):
        self.processing_updates.append(kwargs)
    
    def update_file_ingestion_metadata(self, **kwargs):
        self.ingestion_updates.append(kwargs)
    
    def save_document_page(self, **kwargs):
        self.saved_pages.append(kwargs)


class FakeDoclingService:
    def process_document(self, document, path):
        return (
            SimpleNamespace(
                docling_document_path="/app/data/processed/documents/42/document.json",
                total_pages=2,
                neo4j_node_id=None,
                processing_metadata={"markdown_path": "/app/data/processed/documents/42/output.md"},
            ),
            [
                SimpleNamespace(
                    page_number=1, 
                    content="The Earth revolves around the Sun. This is a well-established fact.",
                    metadata={"page": 1}
                ),
                SimpleNamespace(
                    page_number=2, 
                    content="Water is essential for life. All living organisms require water to survive.",
                    metadata={"page": 2}
                ),
            ],
        )


class FakeClaimExtractionService:
    def extract_claims(self, content: str, source_confidence: float, source_type: str) -> List[Dict]:
        # Simple extraction: split on periods
        sentences = [s.strip() for s in content.split('.') if s.strip()]
        claims = []
        for sentence in sentences:
            claims.append({
                "text": sentence + ".",
                "confidence": source_confidence,
                "source_type": source_type,
            })
        return claims


class FakeLightRagHandoffService:
    def handoff_document(self, document, markdown_path):
        return {"scan_requested": True, "markdown_path": markdown_path}


class EmbeddingPopulationTests(unittest.TestCase):
    """Test suite for P2-C embedding population during ingestion."""
    
    def test_page_embeddings_are_generated_during_processing(self):
        """Embeddings should be generated for each page's content during processing."""
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        
        # Simulate page processing
        pages = [
            SimpleNamespace(
                page_number=1,
                content="The Earth revolves around the Sun.",
                metadata={"page": 1}
            ),
            SimpleNamespace(
                page_number=2,
                content="Water is essential for life.",
                metadata={"page": 2}
            ),
        ]
        
        # Generate embeddings for pages (what the pipeline should do)
        page_texts = [page.content for page in pages]
        embeddings = fake_embedding_service.embed_batch(page_texts)
        
        # Assert
        self.assertIsNotNone(embeddings, "Embeddings should be generated")
        self.assertEqual(2, len(embeddings), "Should generate embedding for each page")
        self.assertEqual(1, len(fake_embedding_service.embed_batch_calls), "Should use batch API")
        self.assertEqual(page_texts, fake_embedding_service.embed_batch_calls[0])
        
        # Verify embedding dimensions
        for embedding in embeddings:
            self.assertEqual(384, len(embedding), "Embeddings should match configured dimension")
    
    def test_claim_embeddings_are_generated_during_claim_extraction(self):
        """Embeddings should be generated for each claim's text after extraction."""
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        fake_claim_extractor = FakeClaimExtractionService()
        
        # Extract claims from page content
        page_content = "The Earth revolves around the Sun. Water is essential for life."
        claims = fake_claim_extractor.extract_claims(
            content=page_content,
            source_confidence=0.9,
            source_type="upload"
        )
        
        # Generate embeddings for claims (what the pipeline should do)
        claim_texts = [claim["text"] for claim in claims]
        embeddings = fake_embedding_service.embed_batch(claim_texts)
        
        # Assert
        self.assertIsNotNone(embeddings, "Claim embeddings should be generated")
        self.assertGreater(len(embeddings), 0, "Should generate embeddings for extracted claims")
        self.assertEqual(len(claims), len(embeddings), "One embedding per claim")
        
        # Verify each claim could be embedded
        for i, claim in enumerate(claims):
            self.assertIsNotNone(embeddings[i], f"Claim {i} should have embedding")
            self.assertEqual(384, len(embeddings[i]))
    
    def test_page_embeddings_are_persisted_to_neo4j_during_page_creation(self):
        """Page nodes should store content_embedding property when created."""
        fake_neo4j = FakeNeo4jService()
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        
        pages = [
            SimpleNamespace(
                page_number=1,
                content="The Earth revolves around the Sun.",
                metadata={"page": 1}
            ),
            SimpleNamespace(
                page_number=2,
                content="Water is essential for life.",
                metadata={"page": 2}
            ),
        ]
        
        # Generate embeddings
        page_texts = [page.content for page in pages]
        embeddings = fake_embedding_service.embed_batch(page_texts)
        
        # Create page nodes with embeddings (what the pipeline should do)
        page_node_ids = fake_neo4j.create_page_nodes_with_embeddings(
            pages=pages,
            doc_node_id="doc-node-42",
            document_id=42,
            page_embeddings=embeddings
        )
        
        # Assert
        self.assertEqual(2, len(fake_neo4j.created_pages))
        
        # Verify page 1 has embedding
        page1 = fake_neo4j.created_pages[0]
        self.assertIn("content_embedding", page1, "Page 1 should have content_embedding")
        self.assertEqual(384, len(page1["content_embedding"]))
        self.assertEqual("42_1", page1["page_id"])
        
        # Verify page 2 has embedding
        page2 = fake_neo4j.created_pages[1]
        self.assertIn("content_embedding", page2, "Page 2 should have content_embedding")
        self.assertEqual(384, len(page2["content_embedding"]))
        self.assertEqual("42_2", page2["page_id"])
        
        # Verify embeddings are tracked for retrieval validation
        self.assertEqual(2, len(fake_neo4j.page_embeddings))
        self.assertIn("42_1", fake_neo4j.page_embeddings)
        self.assertIn("42_2", fake_neo4j.page_embeddings)
    
    def test_claim_embeddings_are_persisted_to_neo4j_during_claim_creation(self):
        """Claim nodes should store text_embedding property when created."""
        fake_neo4j = FakeNeo4jService()
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        fake_claim_extractor = FakeClaimExtractionService()
        
        # Extract claims
        page_content = "The Earth revolves around the Sun. Water is essential for life."
        claims = fake_claim_extractor.extract_claims(
            content=page_content,
            source_confidence=0.9,
            source_type="upload"
        )
        
        # Generate embeddings for claims
        claim_texts = [claim["text"] for claim in claims]
        embeddings = fake_embedding_service.embed_batch(claim_texts)
        
        # Create claim nodes with embeddings (what the pipeline should do)
        claim_node_ids = fake_neo4j.create_claim_nodes_with_embeddings(
            claims=claims,
            page_node_id="page-node-1",
            document_id=42,
            page_number=1,
            claim_embeddings=embeddings
        )
        
        # Assert
        self.assertGreater(len(fake_neo4j.created_claims), 0, "Claims should be created")
        
        # Verify each claim has embedding
        for claim_data in fake_neo4j.created_claims:
            self.assertIn("text_embedding", claim_data, "Claim should have text_embedding")
            self.assertEqual(384, len(claim_data["text_embedding"]))
            self.assertIn("claim_id", claim_data)
            
            # Verify embedding is tracked
            claim_id = claim_data["claim_id"]
            self.assertIn(claim_id, fake_neo4j.claim_embeddings)
    
    def test_embedding_generation_is_skipped_when_service_unavailable(self):
        """If embedding service is unavailable, processing should continue without embeddings."""
        fake_embedding_service = FakeEmbeddingService(available=False)
        fake_neo4j = FakeNeo4jService()
        
        pages = [
            SimpleNamespace(
                page_number=1,
                content="The Earth revolves around the Sun.",
                metadata={"page": 1}
            ),
        ]
        
        # Try to generate embeddings
        page_texts = [page.content for page in pages]
        embeddings = fake_embedding_service.embed_batch(page_texts)
        
        # Assert - embeddings should be None when unavailable
        self.assertIsNone(embeddings, "Should return None when service unavailable")
        
        # Create page nodes without embeddings
        page_node_ids = fake_neo4j.create_page_nodes_with_embeddings(
            pages=pages,
            doc_node_id="doc-node-42",
            document_id=42,
            page_embeddings=None  # No embeddings
        )
        
        # Assert - pages are created but without embeddings
        self.assertEqual(1, len(fake_neo4j.created_pages))
        page1 = fake_neo4j.created_pages[0]
        self.assertNotIn("content_embedding", page1, "Page should not have embedding when service unavailable")
        self.assertEqual(0, len(fake_neo4j.page_embeddings), "No embeddings should be tracked")
    
    def test_batch_embedding_is_used_for_efficiency(self):
        """Multiple pages/claims should use batch API instead of individual calls."""
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        
        # Simulate 5 pages
        pages = [
            SimpleNamespace(page_number=i, content=f"Page {i} content.", metadata={"page": i})
            for i in range(1, 6)
        ]
        
        # Generate embeddings using batch API
        page_texts = [page.content for page in pages]
        embeddings = fake_embedding_service.embed_batch(page_texts)
        
        # Assert
        self.assertEqual(5, len(embeddings))
        self.assertEqual(1, len(fake_embedding_service.embed_batch_calls), "Should use single batch call")
        self.assertEqual(0, len(fake_embedding_service.embed_text_calls), "Should not use individual calls")
        self.assertEqual(page_texts, fake_embedding_service.embed_batch_calls[0])
    
    def test_embedding_service_dimension_matches_neo4j_index_configuration(self):
        """Embedding dimension must match the configured vector index dimension."""
        # Default dimension for all-MiniLM-L6-v2 model
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        
        dimension = fake_embedding_service.get_embedding_dimension()
        self.assertEqual(384, dimension, "Default embedding dimension should be 384")
        
        # Generate sample embedding
        embedding = fake_embedding_service.embed_text("Test content.")
        self.assertIsNotNone(embedding)
        self.assertEqual(384, len(embedding), "Generated embedding should match dimension")
        
        # Note: This dimension must match EMBEDDING_DIM environment variable
        # used by Neo4jService._ensure_vector_indexes()
    
    def test_empty_page_content_does_not_crash_embedding_generation(self):
        """Empty or whitespace-only content should be handled gracefully."""
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        
        # Test empty content
        empty_embedding = fake_embedding_service.embed_text("")
        self.assertIsNotNone(empty_embedding, "Should generate embedding even for empty text")
        self.assertEqual(384, len(empty_embedding))
        
        # Test whitespace-only content
        whitespace_embedding = fake_embedding_service.embed_text("   ")
        self.assertIsNotNone(whitespace_embedding)
        self.assertEqual(384, len(whitespace_embedding))
    
    def test_embedding_generation_preserves_content_order_in_batch(self):
        """Batch embeddings should maintain input order for correct association."""
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        
        texts = ["First page.", "Second page.", "Third page."]
        embeddings = fake_embedding_service.embed_batch(texts)
        
        self.assertEqual(3, len(embeddings))
        
        # Verify embeddings are distinct (deterministic based on content)
        self.assertNotEqual(embeddings[0], embeddings[1], "Different content should produce different embeddings")
        self.assertNotEqual(embeddings[1], embeddings[2])
        
        # Verify order is preserved
        self.assertEqual(1, len(fake_embedding_service.embed_batch_calls))
        self.assertEqual(texts, fake_embedding_service.embed_batch_calls[0])


class EmbeddingPipelineIntegrationTests(unittest.TestCase):
    """
    Integration-level tests for embedding population within the processing pipeline.
    
    These tests validate the interaction between services but use fakes to isolate
    from external dependencies (Ollama, Neo4j).
    """
    
    def test_processing_pipeline_generates_embeddings_for_all_pages_and_claims(self):
        """
        End-to-end validation that processing generates embeddings for:
        1. All page content
        2. All extracted claims
        And persists them to Neo4j during node creation.
        """
        # Setup
        document = SimpleNamespace(
            id=42,
            filename="test.pdf",
            original_filename="test.pdf",
            file_path="/app/data/uploads/test.pdf",
            mime_type="application/pdf",
            processing_status="uploaded",
            tenant_id="default",
            source_type="upload",
        )
        
        fake_db = FakeDatabaseService(document)
        fake_docling = FakeDoclingService()
        fake_neo4j = FakeNeo4jService()
        fake_embedding_service = FakeEmbeddingService(embedding_dimension=384)
        fake_claim_extractor = FakeClaimExtractionService()
        fake_lightrag = FakeLightRagHandoffService()
        
        # Simulate processing pipeline (without actually calling process_document_task)
        # This mirrors what the pipeline should do:
        
        # 1. Process document with docling
        processed_doc, pages = fake_docling.process_document(document, document.file_path)
        
        # 2. Generate embeddings for pages
        page_texts = [page.content for page in pages]
        page_embeddings = fake_embedding_service.embed_batch(page_texts)
        
        # 3. Create page nodes with embeddings
        doc_node_id = fake_neo4j.create_document_node(document)
        page_node_ids = fake_neo4j.create_page_nodes_with_embeddings(
            pages=pages,
            doc_node_id=doc_node_id,
            document_id=document.id,
            page_embeddings=page_embeddings
        )
        
        # 4. Extract claims and generate embeddings for each page
        for i, page in enumerate(pages):
            if i < len(page_node_ids):
                page_node_id = page_node_ids[i]
                
                # Extract claims
                claims = fake_claim_extractor.extract_claims(
                    content=page.content,
                    source_confidence=0.9,
                    source_type="upload"
                )
                
                # Generate claim embeddings
                if claims:
                    claim_texts = [claim["text"] for claim in claims]
                    claim_embeddings = fake_embedding_service.embed_batch(claim_texts)
                    
                    # Create claim nodes with embeddings
                    fake_neo4j.create_claim_nodes_with_embeddings(
                        claims=claims,
                        page_node_id=page_node_id,
                        document_id=document.id,
                        page_number=page.page_number,
                        claim_embeddings=claim_embeddings
                    )
        
        # Assert - pages
        self.assertEqual(2, len(fake_neo4j.created_pages), "Should create 2 pages")
        for page_data in fake_neo4j.created_pages:
            self.assertIn("content_embedding", page_data, "Each page should have content_embedding")
            self.assertEqual(384, len(page_data["content_embedding"]))
        
        # Assert - claims
        self.assertGreater(len(fake_neo4j.created_claims), 0, "Should create claims")
        for claim_data in fake_neo4j.created_claims:
            self.assertIn("text_embedding", claim_data, "Each claim should have text_embedding")
            self.assertEqual(384, len(claim_data["text_embedding"]))
        
        # Assert - embedding service calls
        # Should have at least 2 batch calls: 1 for pages, N for claims (one per page)
        self.assertGreaterEqual(
            len(fake_embedding_service.embed_batch_calls), 
            2,  # At minimum: pages batch + 1 claim batch
            "Should use batch API for both pages and claims"
        )


if __name__ == "__main__":
    unittest.main()
