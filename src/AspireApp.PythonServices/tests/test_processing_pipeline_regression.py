from __future__ import annotations

import asyncio
import json
import os
import sys
import tempfile
import unittest
import httpx
from datetime import UTC, datetime
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from fastapi import BackgroundTasks, HTTPException

from app.contracts import CanonicalDocument
from app.routers import processing
from app.services.lightrag_handoff_service import LightRagHandoffService
from app.services.url_handlers.base import FetchedContent
from app.services.url_handlers.youtube import YouTubeChannelHandler


class FakeDatabaseService:
    def __init__(self, document: SimpleNamespace | None = None):
        self.document = document
        self.documents: dict[int, SimpleNamespace] = {}
        self.next_document_id = 1
        self.status_updates: list[tuple[int, str, str | None]] = []
        self.indexing_updates: list[tuple[int, str, str | None]] = []
        self.ingestion_updates: list[dict] = []
        self.processing_updates: list[dict] = []
        self.saved_pages: list[dict] = []
        self.file_record: dict | None = None
        self.duplicate_urls: set[str] = set()
        self.added_url_sources: list[dict] = []
        self.youtube_queue_entries: dict[int, dict] = {}
        if document is not None:
            self.register_document(document)

    def register_document(self, document: SimpleNamespace) -> SimpleNamespace:
        if not hasattr(document, "indexing_status"):
            document.indexing_status = "not_requested"
        if not hasattr(document, "indexing_error"):
            document.indexing_error = None
        self.documents[document.id] = document
        self.next_document_id = max(self.next_document_id, document.id + 1)
        return document

    def get_document_by_id(self, document_id: int):
        return self.documents.get(document_id)

    def update_file_status(self, file_id: int, status: str, error: str = None) -> None:
        self.status_updates.append((file_id, status, error))
        document = self.documents.get(file_id)
        if document is not None:
            document.processing_status = status

    def update_file_indexing_status(self, file_id: int, indexing_status: str, error: str = None) -> None:
        self.indexing_updates.append((file_id, indexing_status, error))
        document = self.documents.get(file_id)
        if document is not None:
            document.indexing_status = indexing_status
            document.indexing_error = error

    def resolve_upload_path(self, document):
        return document.file_path

    def find_duplicate_by_url(self, source_url: str, tenant_id: str = "default"):
        for document in self.documents.values():
            if getattr(document, "source_url", None) == source_url and getattr(document, "tenant_id", "default") == tenant_id:
                return {
                    "id": document.id,
                    "source_url": source_url,
                    "tenant_id": tenant_id,
                    "status": getattr(document, "processing_status", "uploaded"),
                }
        if source_url in self.duplicate_urls:
            return {"id": -1, "source_url": source_url, "tenant_id": tenant_id, "status": "processed"}
        return None

    def add_url_datasource(
        self,
        source_name: str,
        source_url: str,
        source_type: str = "url",
        mime_type: str | None = None,
        status: str = "uploaded",
        tenant_id: str = "default",
    ) -> int:
        document_id = self.next_document_id
        self.next_document_id += 1
        self.added_url_sources.append(
            {
                "id": document_id,
                "source_name": source_name,
                "source_url": source_url,
                "source_type": source_type,
                "mime_type": mime_type,
                "status": status,
                "tenant_id": tenant_id,
            }
        )
        resolved_mime_type = mime_type or ("text/plain" if source_type in {"youtube_video", "youtube_channel"} else "text/html")
        self.register_document(
            SimpleNamespace(
                id=document_id,
                filename=source_name,
                original_filename=source_name,
                file_path="",
                mime_type=resolved_mime_type,
                processing_status=status,
                tenant_id=tenant_id,
                source_type=source_type,
                source_url=source_url,
            )
        )
        return document_id

    def update_file_processing_results(self, **kwargs) -> None:
        self.processing_updates.append(kwargs)

    def update_file_ingestion_metadata(self, **kwargs) -> None:
        self.ingestion_updates.append(kwargs)

    def save_document_page(self, **kwargs) -> None:
        self.saved_pages.append(kwargs)

    def list_unprocessed_documents(self):
        return [
            document
            for document in self.documents.values()
            if getattr(document, "processing_status", "uploaded") in {"uploaded", "error"}
            and not (
                getattr(document, "source_type", None) == "youtube_video"
                and self.youtube_queue_entries.get(document.id, {}).get("completed_at") is None
            )
        ]

    def get_processing_status(self, document_id: int):
        return None

    def get_file_by_id(self, file_id: int):
        if self.file_record and self.file_record.get("id") == file_id:
            return self.file_record
        return None

    def enqueue_youtube_transcript(self, *, file_id: int, source_url: str, tenant_id: str = "default") -> int:
        entry = self.youtube_queue_entries.get(file_id)
        if entry is None:
            entry = {
                "id": len(self.youtube_queue_entries) + 1,
                "file_id": file_id,
                "source_url": source_url,
                "tenant_id": tenant_id,
                "queued_at": datetime.now(UTC),
                "last_attempted_at": None,
                "completed_at": None,
                "last_error": None,
            }
            self.youtube_queue_entries[file_id] = entry
        else:
            entry["source_url"] = source_url
            entry["tenant_id"] = tenant_id
            entry["completed_at"] = None
            entry["last_error"] = None
        return entry["id"]

    def mark_youtube_transcript_completed(self, file_id: int) -> None:
        entry = self.youtube_queue_entries.get(file_id)
        if entry is not None:
            entry["completed_at"] = datetime.now(UTC)
            entry["last_error"] = None

    def mark_youtube_transcript_failed(self, file_id: int, error: str) -> None:
        entry = self.youtube_queue_entries.get(file_id)
        if entry is not None:
            entry["completed_at"] = None
            entry["last_error"] = error


class FakeNeo4jService:
    def __init__(self):
        self.deleted_document_ids: list[int] = []
        self.created_documents: list[object] = []
        self.created_claims: list[dict] = []
        self.page_embeddings: dict[str, list[float]] = {}
        self.claim_embeddings: dict[str, list[float]] = {}

    def create_document_node(self, document):
        self.created_documents.append(document)
        return "doc-node"

    def create_page_nodes(self, pages, doc_node_id, document_id):
        return [f"page-node-{page.page_number}" for page in pages]

    def create_relationships(self, doc_node_id, page_node_ids):
        return None

    def create_sequential_relationships(self, page_node_ids):
        return None
    
    def create_claim_nodes(self, claims, page_node_id, document_id, page_number):
        self.created_claims.append({
            "claims": claims,
            "page_node_id": page_node_id,
            "document_id": document_id,
            "page_number": page_number
        })
        return [f"claim-node-{page_number}-{i}" for i in range(len(claims))]
    
    def populate_page_embedding(self, page_node_id: str, embedding: list[float]) -> None:
        """Store page embedding in fake storage"""
        self.page_embeddings[page_node_id] = embedding
    
    def populate_claim_embedding(self, claim_node_id: str, embedding: list[float]) -> None:
        """Store claim embedding in fake storage"""
        self.claim_embeddings[claim_node_id] = embedding

    def delete_document_graph(self, document_id: int):
        self.deleted_document_ids.append(document_id)
        return {"deleted_documents": 1, "deleted_pages": 2}


class FakeEmbeddingService:
    def __init__(self, embedding_dimension: int = 384):
        self.embedding_dimension = embedding_dimension
        self.batch_calls: list[list[str]] = []

    def is_available(self) -> bool:
        return True

    def embed_batch(self, texts: list[str], batch_size: int = 32, show_progress: bool = False):
        self.batch_calls.append(list(texts))
        return [[float(i)] * self.embedding_dimension for i in range(len(texts))]

    def embed_text(self, text: str):
        raise AssertionError("embed_text should not be called when batch embedding is available")


class FakeClaimExtractionService:
    def extract_claims(self, content: str, source_confidence: float, source_type: str):
        return [
            {
                "text": f"{content} claim 1",
                "confidence": source_confidence,
                "source_type": source_type,
            },
            {
                "text": f"{content} claim 2",
                "confidence": source_confidence,
                "source_type": source_type,
            },
        ]


class FakeLightRagHandoffService:
    def __init__(
        self,
        readiness_updates: list[tuple[str, str | None]] | None = None,
        *,
        scan_requested: bool = True,
    ):
        self.cleaned_documents: list[tuple[int, str | None]] = []
        self.observed_processing_statuses: list[str | None] = []
        self.readiness_updates = readiness_updates or [
            ("queued", None),
            ("indexing", None),
            ("ready", None),
        ]
        self.scan_requested = scan_requested
        self._last_document = None

    def handoff_document(self, document, markdown_path):
        self._last_document = document
        return {
            "scan_requested": self.scan_requested,
            "markdown_path": markdown_path,
            "staged_input_path": f"/app/data/inputs/{document.id:06d}-test.md",
        }

    def wait_for_document_readiness(self, staged_input_path, *, status_callback=None, **_kwargs):
        self.observed_processing_statuses.append(getattr(self._last_document, "processing_status", None))
        result = {
            "indexing_status": "not_requested",
            "indexing_error": None,
            "document_status": None,
        }
        for status, error in self.readiness_updates:
            if status_callback is not None:
                status_callback(status, error)
            result = {
                "indexing_status": status,
                "indexing_error": error,
                "document_status": {"file_path": staged_input_path, "status": status},
            }
        return result

    def cleanup_document(self, document, staged_input_path=None, delete_llm_cache=False, wait_timeout_seconds=30.0):
        self.cleaned_documents.append((document.id, staged_input_path))
        return {"doc_ids": ["doc_123"], "removed_paths": [staged_input_path] if staged_input_path else []}


class FakeAsyncHttpClient:
    def __init__(self, responses: dict[str, httpx.Response | list[httpx.Response]]):
        self.responses = responses
        self.requested_urls: list[str] = []

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc, tb):
        return False

    async def get(self, url: str):
        normalized_url = str(url)
        self.requested_urls.append(normalized_url)
        if normalized_url not in self.responses:
            raise AssertionError(f"Unexpected GET {normalized_url}")

        response = self.responses[normalized_url]
        if isinstance(response, list):
            if not response:
                raise AssertionError(f"No more stubbed responses for {normalized_url}")
            return response.pop(0)

        return response


class StubbedYouTubeChannelHandler(YouTubeChannelHandler):
    def __init__(self, client: FakeAsyncHttpClient, max_videos: int = 50):
        super().__init__(max_videos=max_videos)
        self._client = client

    def _create_http_client(self):
        return self._client


class ProcessingPipelineRegressionTests(unittest.TestCase):
    def test_lightrag_handoff_waits_for_service_ready_before_scan(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            markdown_path = Path(temp_dir) / "processing-smoke.md"
            markdown_path.write_text("# test", encoding="utf-8")
            document = SimpleNamespace(
                id=42,
                filename="processing-smoke.pdf",
                original_filename="processing-smoke.pdf",
            )

            class SequencedLightRagHandoffService(LightRagHandoffService):
                def __init__(self, input_dir: Path):
                    super().__init__(input_dir=input_dir, service_url="http://lightrag.test", scan_timeout_seconds=1.0)
                    self.document_requests = 0
                    self.scan_requests = 0

                def _json_request(self, method: str, path: str, payload=None, timeout=None):
                    if method == "GET" and path == "/documents":
                        self.document_requests += 1
                        if self.document_requests < 3:
                            raise RuntimeError("LightRAG is still starting")
                        return {"statuses": {}}

                    if method == "POST" and path == "/documents/scan":
                        self.scan_requests += 1
                        return {"status": "accepted"}

                    raise AssertionError(f"Unexpected request {method} {path}")

            handoff_service = SequencedLightRagHandoffService(Path(temp_dir) / "inputs")

            with patch("app.services.lightrag_handoff_service.time.sleep", return_value=None):
                handoff = handoff_service.handoff_document(document, markdown_path)

            self.assertTrue(handoff["scan_requested"])
            self.assertEqual(3, handoff_service.document_requests)
            self.assertEqual(1, handoff_service.scan_requests)
            self.assertTrue((Path(temp_dir) / "inputs" / "000042-processing-smoke.md").exists())

    def test_lightrag_readiness_poll_uses_per_document_status(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            staged_input_path = Path(temp_dir) / "inputs" / "000042-processing-smoke.md"
            staged_input_path.parent.mkdir(parents=True, exist_ok=True)
            staged_input_path.write_text("# staged", encoding="utf-8")
            observed_updates: list[tuple[str, str | None]] = []

            class PollingLightRagHandoffService(LightRagHandoffService):
                def __init__(self, input_dir: Path):
                    super().__init__(
                        input_dir=input_dir,
                        service_url="http://lightrag.test",
                        scan_timeout_seconds=1.0,
                        readiness_timeout_seconds=5.0,
                        readiness_poll_interval_seconds=0.1,
                    )
                    self.document_requests = 0

                def _json_request(self, method: str, path: str, payload=None, timeout=None):
                    if method == "GET" and path == "/documents":
                        self.document_requests += 1
                        if self.document_requests == 1:
                            return {"statuses": {"pending": [{"id": "doc-1", "file_path": staged_input_path.name}]}}
                        if self.document_requests == 2:
                            return {"statuses": {"processing": [{"id": "doc-1", "file_path": staged_input_path.name}]}}
                        return {"statuses": {"processed": [{"id": "doc-1", "file_path": staged_input_path.name}]}}

                    raise AssertionError(f"Unexpected request {method} {path}")

            handoff_service = PollingLightRagHandoffService(staged_input_path.parent)

            with patch("app.services.lightrag_handoff_service.time.sleep", return_value=None):
                readiness = handoff_service.wait_for_document_readiness(
                    staged_input_path,
                    status_callback=lambda status, error: observed_updates.append((status, error)),
                )

            self.assertEqual("ready", readiness["indexing_status"])
            self.assertEqual(
                [("queued", None), ("indexing", None), ("ready", None)],
                observed_updates,
            )
            self.assertEqual(3, handoff_service.document_requests)

    def test_lightrag_readiness_uses_local_status_store_when_http_status_is_stale(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            staged_input_path = Path(temp_dir) / "inputs" / "000062-docs-github.md"
            staged_input_path.parent.mkdir(parents=True, exist_ok=True)
            staged_input_path.write_text("# staged", encoding="utf-8")
            doc_status_store_path = Path(temp_dir) / "rag_storage" / "kv_store_doc_status.json"
            doc_status_store_path.parent.mkdir(parents=True, exist_ok=True)
            doc_status_store_path.write_text(
                json.dumps(
                    {
                        "doc-62": {
                            "id": "doc-62",
                            "file_path": staged_input_path.name,
                            "status": "processed",
                            "updated_at": "2026-04-21T13:16:12.483483+00:00",
                        }
                    }
                ),
                encoding="utf-8",
            )

            class StaleHttpLightRagHandoffService(LightRagHandoffService):
                def __init__(self, input_dir: Path, status_store: Path):
                    super().__init__(
                        input_dir=input_dir,
                        service_url="http://lightrag.test",
                        scan_timeout_seconds=1.0,
                        doc_status_store_path=status_store,
                    )

                def _json_request(self, method: str, path: str, payload=None, timeout=None):
                    if method == "GET" and path == "/documents":
                        return {
                            "statuses": {
                                "pending": [
                                    {
                                        "id": "doc-62",
                                        "file_path": staged_input_path.name,
                                        "status": "pending",
                                    }
                                ]
                            }
                        }

                    raise AssertionError(f"Unexpected request {method} {path}")

            handoff_service = StaleHttpLightRagHandoffService(staged_input_path.parent, doc_status_store_path)
            readiness = handoff_service.get_document_status_by_file_path(staged_input_path)

            self.assertIsNotNone(readiness)
            self.assertEqual("processed", readiness["status"])

    def test_process_document_task_persists_pages_and_marks_processed(self):
        document = SimpleNamespace(
            id=42,
            filename="science-textbook.pdf",
            original_filename="science-textbook.pdf",
            file_path="C:\\data\\uploads\\stored-file.pdf",
            mime_type="application/pdf",
            processing_status="uploaded",
            tenant_id="default",
            source_type="upload",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()
        docling = SimpleNamespace(
            process_document=lambda doc, path: (
                SimpleNamespace(
                    docling_document_path="/app/data/processed/documents/42/document.json",
                    total_pages=2,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": "/app/data/processed/documents/42/output.md"},
                ),
                [
                    SimpleNamespace(page_number=1, content="The Earth revolves around the Sun. This is a well-established fact.", metadata={"page": 1}),
                    SimpleNamespace(page_number=2, content="Water is essential for life. All living organisms require water to survive.", metadata={"page": 2}),
                ],
            )
        )
        fake_embedding_service = FakeEmbeddingService()
        fake_claim_extractor = FakeClaimExtractionService()

        with patch.dict(os.environ, {"ENABLE_INGESTION_VECTOR_POPULATION": "true"}, clear=False), \
            patch("app.routers.processing.EmbeddingService", return_value=fake_embedding_service), \
            patch("app.routers.processing.ClaimExtractionService", return_value=fake_claim_extractor):
            asyncio.run(
                processing.process_document_task(
                    document_id=42,
                    db=db,
                    docling=docling,
                    neo4j=neo4j,
                    lightrag_handoff=FakeLightRagHandoffService(),
                )
            )

        self.assertEqual((42, "processing", None), db.status_updates[0])
        self.assertEqual((42, "processed", None), db.status_updates[-1])
        self.assertEqual(
            [(42, "queued", None), (42, "indexing", None), (42, "ready", None)],
            db.indexing_updates,
        )
        self.assertEqual(1, len(db.ingestion_updates))
        self.assertEqual(1, len(db.processing_updates))
        self.assertEqual(2, len(db.saved_pages))
        self.assertEqual("page-node-1", db.saved_pages[0]["neo4j_node_id"])
        self.assertEqual("Water is essential for life. All living organisms require water to survive.", db.saved_pages[1]["content"])
        self.assertEqual(0.9, db.ingestion_updates[0]["source_confidence"])
        self.assertIsInstance(neo4j.created_documents[0], CanonicalDocument)
        
        # Verify claim extraction was called
        self.assertGreater(len(neo4j.created_claims), 0, "Claims should be extracted from pages")
        self.assertEqual(2, len(neo4j.created_claims), "Should extract claims from both pages")
        
        # Verify claims were extracted for page 1
        page1_claims = neo4j.created_claims[0]
        self.assertEqual(42, page1_claims["document_id"])
        self.assertEqual(1, page1_claims["page_number"])
        self.assertEqual("page-node-1", page1_claims["page_node_id"])
        self.assertGreater(len(page1_claims["claims"]), 0, "Page 1 should have extracted claims")
        
        # Verify claims were extracted for page 2
        page2_claims = neo4j.created_claims[1]
        self.assertEqual(42, page2_claims["document_id"])
        self.assertEqual(2, page2_claims["page_number"])
        self.assertEqual("page-node-2", page2_claims["page_node_id"])
        self.assertGreater(len(page2_claims["claims"]), 0, "Page 2 should have extracted claims")
        
        # Verify embedding generation and persistence
        self.assertEqual(3, len(fake_embedding_service.batch_calls), "Expected batch embeddings for pages and claims")
        expected_page_texts = [
            "The Earth revolves around the Sun. This is a well-established fact.",
            "Water is essential for life. All living organisms require water to survive.",
        ]
        self.assertEqual(expected_page_texts, fake_embedding_service.batch_calls[0])
        self.assertEqual(2, len(neo4j.page_embeddings))
        self.assertIn("page-node-1", neo4j.page_embeddings)
        self.assertIn("page-node-2", neo4j.page_embeddings)
        self.assertEqual(4, len(neo4j.claim_embeddings))
        for embedding in neo4j.claim_embeddings.values():
            self.assertEqual(384, len(embedding))

    def test_process_document_task_skips_optional_vector_population_by_default(self):
        document = SimpleNamespace(
            id=46,
            filename="upload-timeout.pdf",
            original_filename="upload-timeout.pdf",
            file_path="C:\\data\\uploads\\upload-timeout.pdf",
            mime_type="application/pdf",
            processing_status="uploaded",
            tenant_id="default",
            source_type="upload",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()
        docling = SimpleNamespace(
            process_document=lambda doc, path: (
                SimpleNamespace(
                    docling_document_path="/app/data/processed/documents/46/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": "/app/data/processed/documents/46/output.md"},
                ),
                [SimpleNamespace(page_number=1, content="Upload timeout regression content.", metadata={"page": 1})],
            )
        )

        with patch.dict(os.environ, {"ENABLE_INGESTION_VECTOR_POPULATION": "false"}, clear=False), \
            patch("app.routers.processing.EmbeddingService", side_effect=AssertionError("EmbeddingService should stay off by default")), \
            patch("app.routers.processing.ClaimExtractionService", return_value=FakeClaimExtractionService()):
            asyncio.run(
                processing.process_document_task(
                    document_id=46,
                    db=db,
                    docling=docling,
                    neo4j=neo4j,
                    lightrag_handoff=FakeLightRagHandoffService(),
                )
            )

        self.assertEqual((46, "processed", None), db.status_updates[-1])
        self.assertEqual((46, "ready", None), db.indexing_updates[-1])
        self.assertEqual({}, neo4j.page_embeddings)
        self.assertEqual({}, neo4j.claim_embeddings)

    def test_process_document_task_marks_timed_out_when_lightrag_readiness_times_out(self):
        document = SimpleNamespace(
            id=43,
            filename="delayed-indexing.pdf",
            original_filename="delayed-indexing.pdf",
            file_path="C:\\data\\uploads\\delayed-file.pdf",
            mime_type="application/pdf",
            processing_status="uploaded",
            tenant_id="default",
            source_type="upload",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()
        docling = SimpleNamespace(
            process_document=lambda doc, path: (
                SimpleNamespace(
                    docling_document_path="/app/data/processed/documents/43/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": "/app/data/processed/documents/43/output.md"},
                ),
                [SimpleNamespace(page_number=1, content="Delayed indexing content.", metadata={"page": 1})],
            )
        )

        with patch("app.routers.processing.EmbeddingService", return_value=FakeEmbeddingService()), \
            patch("app.routers.processing.ClaimExtractionService", return_value=FakeClaimExtractionService()):
            asyncio.run(
                processing.process_document_task(
                    document_id=43,
                    db=db,
                    docling=docling,
                    neo4j=neo4j,
                    lightrag_handoff=FakeLightRagHandoffService(
                        readiness_updates=[
                            ("queued", None),
                            ("indexing", None),
                            ("timed_out", "Timed out waiting for LightRAG readiness"),
                        ]
                    ),
                )
            )

        self.assertEqual((43, "processed", None), db.status_updates[-1])
        self.assertEqual(("timed_out", "Timed out waiting for LightRAG readiness"), db.indexing_updates[-1][1:])

    def test_process_document_task_keeps_indexing_active_and_reconciles_after_deferred_timeout(self):
        document = SimpleNamespace(
            id=62,
            filename="docs-github.md",
            original_filename="docs-github.md",
            file_path="C:\\data\\uploads\\docs-github.md",
            mime_type="text/markdown",
            processing_status="uploaded",
            tenant_id="default",
            source_type="upload",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()
        docling = SimpleNamespace(
            process_document=lambda doc, path: (
                SimpleNamespace(
                    docling_document_path="/app/data/processed/documents/62/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": "/app/data/processed/documents/62/output.md"},
                ),
                [SimpleNamespace(page_number=1, content="Deferred indexing content.", metadata={"page": 1})],
            )
        )

        class DeferredReadyLightRagHandoffService(FakeLightRagHandoffService):
            def __init__(self):
                super().__init__(readiness_updates=[("queued", None), ("indexing", None)])
                self.readiness_call_count = 0

            def wait_for_document_readiness(self, staged_input_path, *, status_callback=None, **_kwargs):
                self.readiness_call_count += 1
                if self.readiness_call_count == 1:
                    for status, error in self.readiness_updates:
                        if status_callback is not None:
                            status_callback(status, error)
                    return {
                        "indexing_status": "timed_out",
                        "indexing_error": "Timed out after 300s waiting for LightRAG readiness",
                        "document_status": {"file_path": staged_input_path, "status": "pending"},
                        "readiness_deferred": True,
                        "last_known_indexing_status": "indexing",
                        "readiness_timeout_message": "Timed out after 300s waiting for LightRAG readiness",
                    }

                if status_callback is not None:
                    status_callback("ready", None)
                return {
                    "indexing_status": "ready",
                    "indexing_error": None,
                    "document_status": {"file_path": staged_input_path, "status": "processed"},
                }

        lightrag_handoff = DeferredReadyLightRagHandoffService()

        def run_reconciler_immediately(*, document_id, staged_input_path, db, lightrag_handoff, docling_document_path, remaining_attempts=1):
            processing._continue_lightrag_readiness_sync(
                document_id=document_id,
                staged_input_path=staged_input_path,
                db=db,
                lightrag_handoff=lightrag_handoff,
                docling_document_path=docling_document_path,
                remaining_attempts=remaining_attempts,
            )

        with patch("app.routers.processing.EmbeddingService", return_value=FakeEmbeddingService()), \
            patch("app.routers.processing.ClaimExtractionService", return_value=FakeClaimExtractionService()), \
            patch("app.routers.processing._start_lightrag_readiness_reconciler", side_effect=run_reconciler_immediately), \
            patch("app.routers.processing._update_lightrag_metadata"):
            asyncio.run(
                processing.process_document_task(
                    document_id=62,
                    db=db,
                    docling=docling,
                    neo4j=neo4j,
                    lightrag_handoff=lightrag_handoff,
                )
            )

        self.assertEqual((62, "processed", None), db.status_updates[-1])
        self.assertEqual((62, "ready", None), db.indexing_updates[-1])
        self.assertIn((62, "indexing", None), db.indexing_updates)
        self.assertNotIn((62, "timed_out", "Timed out after 300s waiting for LightRAG readiness"), db.indexing_updates)
        self.assertEqual(2, lightrag_handoff.readiness_call_count)

    def test_process_document_task_marks_failed_when_lightrag_readiness_reports_failed_status(self):
        document = SimpleNamespace(
            id=44,
            filename="failed-indexing.pdf",
            original_filename="failed-indexing.pdf",
            file_path="C:\\data\\uploads\\failed-file.pdf",
            mime_type="application/pdf",
            processing_status="uploaded",
            tenant_id="default",
            source_type="upload",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()
        docling = SimpleNamespace(
            process_document=lambda doc, path: (
                SimpleNamespace(
                    docling_document_path="/app/data/processed/documents/44/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": "/app/data/processed/documents/44/output.md"},
                ),
                [SimpleNamespace(page_number=1, content="Failed indexing content.", metadata={"page": 1})],
            )
        )

        with patch("app.routers.processing.EmbeddingService", return_value=FakeEmbeddingService()), \
            patch("app.routers.processing.ClaimExtractionService", return_value=FakeClaimExtractionService()):
            asyncio.run(
                processing.process_document_task(
                    document_id=44,
                    db=db,
                    docling=docling,
                    neo4j=neo4j,
                    lightrag_handoff=FakeLightRagHandoffService(
                        readiness_updates=[
                            ("queued", None),
                            ("indexing", None),
                            ("failed", "LightRAG indexing failed"),
                        ]
                    ),
                )
            )

        self.assertEqual((44, "processed", None), db.status_updates[-1])
        self.assertEqual(("failed", "LightRAG indexing failed"), db.indexing_updates[-1][1:])

    def test_process_document_task_marks_not_requested_when_markdown_export_is_missing(self):
        document = SimpleNamespace(
            id=45,
            filename="no-markdown-export.pdf",
            original_filename="no-markdown-export.pdf",
            file_path="C:\\data\\uploads\\no-markdown-file.pdf",
            mime_type="application/pdf",
            processing_status="uploaded",
            tenant_id="default",
            source_type="upload",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()
        docling = SimpleNamespace(
            process_document=lambda doc, path: (
                SimpleNamespace(
                    docling_document_path="/app/data/processed/documents/45/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={},
                ),
                [SimpleNamespace(page_number=1, content="Markdown export unavailable.", metadata={"page": 1})],
            )
        )
        lightrag_handoff = FakeLightRagHandoffService()

        with patch("app.routers.processing.EmbeddingService", return_value=FakeEmbeddingService()), \
            patch("app.routers.processing.ClaimExtractionService", return_value=FakeClaimExtractionService()):
            asyncio.run(
                processing.process_document_task(
                    document_id=45,
                    db=db,
                    docling=docling,
                    neo4j=neo4j,
                    lightrag_handoff=lightrag_handoff,
                )
            )

        self.assertEqual((45, "processed", None), db.status_updates[-1])
        self.assertEqual([(45, "not_requested", None)], db.indexing_updates)
        self.assertEqual([], lightrag_handoff.observed_processing_statuses)

    def test_process_document_task_fetches_classified_url_sources_and_enqueues_child_transcripts(self):
        document = SimpleNamespace(
            id=77,
            filename="happy-gilmore-channel",
            original_filename="happy-gilmore-channel",
            file_path="",
            mime_type="text/html",
            processing_status="uploaded",
            tenant_id="tenant-a",
            source_type="youtube_channel",
            source_url="https://www.youtube.com/@happy-gilmore/videos",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()

        class FakeFetcher:
            async def fetch(self, url: str):
                if "watch?v=" in url:
                    video_id = url.split("=")[-1]
                    return FetchedContent(
                        text=f"Transcript for {video_id}",
                        content_type="youtube_transcript",
                        metadata={"title": f"Video {video_id}", "source_type": "youtube_video"},
                    )
                return FetchedContent(
                    text="",
                    content_type="youtube_channel",
                    metadata={"title": "Happy Gilmore", "source_type": "youtube_channel"},
                    child_urls=[
                        "https://www.youtube.com/watch?v=aaaaaaaaaaa",
                        "https://www.youtube.com/watch?v=bbbbbbbbbbb",
                    ],
                )

            def get_handler(self, url: str):
                if "watch?v=" in url:
                    return SimpleNamespace(handler_name="youtube_video")
                return SimpleNamespace(handler_name="youtube_channel")

        processed_documents: list[int] = []
        captured_paths: list[tuple[int, Path]] = []

        def process_document(doc, path):
            path_value = Path(path)
            processed_documents.append(doc.id)
            captured_paths.append((doc.id, path_value))
            self.assertTrue(path_value.exists())
            self.assertEqual(".md", path_value.suffix)
            saved_text = path_value.read_text(encoding="utf-8")
            if doc.id == 77:
                self.assertIn("# Type: youtube_channel", saved_text)
                self.assertIn("Queued child URLs for processing:", saved_text)
                self.assertIn("https://www.youtube.com/watch?v=aaaaaaaaaaa", saved_text)
                page_content = "Queued child URLs for processing."
            else:
                self.assertIn("# Type: youtube_video", saved_text)
                self.assertIn("Transcript for", saved_text)
                page_content = f"Transcript for {doc.source_url}"
            return (
                SimpleNamespace(
                    docling_document_path=f"/app/data/processed/documents/{doc.id}/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": f"/app/data/processed/documents/{doc.id}/output.md"},
                ),
                [SimpleNamespace(page_number=1, content=page_content, metadata={"page": 1})],
            )

        docling = SimpleNamespace(process_document=process_document)

        with tempfile.TemporaryDirectory() as temp_dir, \
            patch.dict(os.environ, {"ASPIRE_DATA_PATH": temp_dir}, clear=False), \
            patch("app.routers.processing.get_url_content_fetcher", return_value=FakeFetcher()), \
            patch("app.routers.processing._ensure_youtube_transcript_queue_drainer"):
            asyncio.run(
                processing.process_document_task(
                    document_id=77,
                    db=db,
                    docling=docling,
                    neo4j=neo4j,
                    lightrag_handoff=FakeLightRagHandoffService(),
                )
            )

        queued_child_ids = [item["id"] for item in db.added_url_sources]
        self.assertEqual((77, "processing", None), db.status_updates[0])
        self.assertIn((77, "processed", None), db.status_updates)
        self.assertEqual((77, "ready", None), db.indexing_updates[-1])
        self.assertEqual(2, len(db.added_url_sources))
        self.assertEqual(2, len(queued_child_ids))
        self.assertEqual([77], processed_documents)
        for child_id in queued_child_ids:
            self.assertIn(child_id, db.youtube_queue_entries)
            self.assertIsNone(db.youtube_queue_entries[child_id]["completed_at"])
        self.assertEqual(
            ["youtube_video", "youtube_video"],
            [item["source_type"] for item in db.added_url_sources],
        )
        self.assertTrue(captured_paths)

    def test_process_document_task_reuses_retryable_child_url_records(self):
        document = SimpleNamespace(
            id=88,
            filename="retryable-channel",
            original_filename="retryable-channel",
            file_path="",
            mime_type="text/html",
            processing_status="uploaded",
            tenant_id="tenant-a",
            source_type="youtube_channel",
            source_url="https://www.youtube.com/@retryable/videos",
        )
        existing_child = SimpleNamespace(
            id=12,
            filename="retryable-video",
            original_filename="retryable-video",
            file_path="",
            mime_type="text/plain",
            processing_status="uploaded",
            tenant_id="tenant-a",
            source_type="youtube_video",
            source_url="https://www.youtube.com/watch?v=aaaaaaaaaaa",
        )
        db = FakeDatabaseService(document)
        db.register_document(existing_child)
        neo4j = FakeNeo4jService()

        class FakeFetcher:
            async def fetch(self, url: str):
                if "watch?v=" in url:
                    return FetchedContent(
                        text="Recovered transcript text",
                        content_type="youtube_transcript",
                        metadata={"source_type": "youtube_video"},
                    )
                return FetchedContent(
                    text="",
                    content_type="youtube_channel",
                    metadata={"source_type": "youtube_channel"},
                    child_urls=[existing_child.source_url],
                )

            def get_handler(self, url: str):
                if "watch?v=" in url:
                    return SimpleNamespace(handler_name="youtube_video")
                return SimpleNamespace(handler_name="youtube_channel")

        processed_documents: list[int] = []

        def process_document(doc, path):
            processed_documents.append(doc.id)
            return (
                SimpleNamespace(
                    docling_document_path=f"/app/data/processed/documents/{doc.id}/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": f"/app/data/processed/documents/{doc.id}/output.md"},
                ),
                [SimpleNamespace(page_number=1, content=f"Processed {doc.id}", metadata={"page": 1})],
            )

        with tempfile.TemporaryDirectory() as temp_dir, \
            patch.dict(os.environ, {"ASPIRE_DATA_PATH": temp_dir}, clear=False), \
            patch("app.routers.processing.get_url_content_fetcher", return_value=FakeFetcher()), \
            patch("app.routers.processing._ensure_youtube_transcript_queue_drainer"):
            asyncio.run(
                processing.process_document_task(
                    document_id=88,
                    db=db,
                    docling=SimpleNamespace(process_document=process_document),
                    neo4j=neo4j,
                    lightrag_handoff=FakeLightRagHandoffService(),
                )
            )

        self.assertEqual([], db.added_url_sources)
        self.assertEqual([88], processed_documents)
        self.assertIn(12, db.youtube_queue_entries)
        self.assertIsNone(db.youtube_queue_entries[12]["completed_at"])

    def test_youtube_channel_handler_retries_past_consent_and_expands_child_videos(self):
        requested_url = "https://www.youtube.com/@csharpfritz/videos"
        resolved_channel_id = "UCfvJirlbRTN-bU9sMWMb_ZQ"
        consent_url = (
            "https://consent.youtube.com/ml"
            "?continue=https://www.youtube.com/@csharpfritz/videos"
        )

        def build_response(url: str, body: str) -> httpx.Response:
            return httpx.Response(200, text=body, request=httpx.Request("GET", url))

        channel_html = f"""
        <html>
          <head><title>Fritz's Tech Tips and Chatter - YouTube</title></head>
          <body>
            "channelMetadataRenderer":{{"title":"Fritz's Tech Tips and Chatter","externalId":"{resolved_channel_id}"}}
            "videoId":"aaaaaaaaaaa"
            "videoId":"bbbbbbbbbbb"
            "videoId":"aaaaaaaaaaa"
          </body>
        </html>
        """
        feed_xml = f"""<?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns="http://www.w3.org/2005/Atom">
          <title>Fritz's Tech Tips and Chatter</title>
          <entry><yt:videoId>bbbbbbbbbbb</yt:videoId></entry>
          <entry><yt:videoId>ccccccccccc</yt:videoId></entry>
        </feed>"""

        fake_client = FakeAsyncHttpClient(
            {
                requested_url: [
                    build_response(consent_url, "<html><body>Before you continue to YouTube</body></html>"),
                    build_response(requested_url, channel_html),
                ],
                f"https://www.youtube.com/feeds/videos.xml?channel_id={resolved_channel_id}": build_response(
                    f"https://www.youtube.com/feeds/videos.xml?channel_id={resolved_channel_id}",
                    feed_xml,
                ),
            }
        )

        handler = StubbedYouTubeChannelHandler(fake_client)

        content = asyncio.run(handler.fetch(requested_url, "unused"))

        self.assertEqual("youtube_channel", content.content_type)
        self.assertEqual(
            [
                "https://www.youtube.com/watch?v=bbbbbbbbbbb",
                "https://www.youtube.com/watch?v=ccccccccccc",
                "https://www.youtube.com/watch?v=aaaaaaaaaaa",
            ],
            content.child_urls,
        )
        self.assertEqual(resolved_channel_id, content.metadata["channel_id"])
        self.assertEqual("csharpfritz", content.metadata["channel_reference"])
        self.assertEqual(
            [
                requested_url,
                requested_url,
                f"https://www.youtube.com/feeds/videos.xml?channel_id={resolved_channel_id}",
            ],
            fake_client.requested_urls,
        )

    def test_youtube_channel_handler_preserves_explicit_failure_when_channel_unresolved(self):
        requested_url = "https://www.youtube.com/@missing-channel/videos"

        def build_response(url: str, body: str) -> httpx.Response:
            return httpx.Response(200, text=body, request=httpx.Request("GET", url))

        fake_client = FakeAsyncHttpClient(
            {
                requested_url: build_response(
                    requested_url,
                    "<html><head><title>Missing Channel - YouTube</title></head><body>No channel metadata here.</body></html>",
                ),
            }
        )
        handler = StubbedYouTubeChannelHandler(fake_client)

        with self.assertRaises(RuntimeError) as context:
            asyncio.run(handler.fetch(requested_url, "unused"))

        self.assertEqual("Could not resolve YouTube channel missing-channel", str(context.exception))

    def test_process_document_task_marks_error_when_docling_fails(self):
        document = SimpleNamespace(
            id=7,
            file_path="C:\\data\\uploads\\broken.pdf",
            processing_status="uploaded",
        )
        db = FakeDatabaseService(document)
        neo4j = FakeNeo4jService()

        class ExplodingDocling:
            def process_document(self, document, path):
                raise RuntimeError("docling exploded")

        with self.assertRaises(RuntimeError):
            asyncio.run(
                processing.process_document_task(
                    document_id=7,
                    db=db,
                    docling=ExplodingDocling(),
                    neo4j=neo4j,
                )
            )

        self.assertEqual((7, "processing", None), db.status_updates[0])
        self.assertEqual((7, "error", "docling exploded"), db.status_updates[-1])

    def test_process_document_endpoint_rejects_duplicate_processing(self):
        document = SimpleNamespace(id=5, processing_status="processing")
        db = FakeDatabaseService(document)

        with self.assertRaises(HTTPException) as context:
            asyncio.run(
                processing.process_document(
                    document_id=5,
                    background_tasks=BackgroundTasks(),
                    db=db,
                    neo4j=FakeNeo4jService(),
                )
            )

        self.assertEqual(409, context.exception.status_code)

    def test_process_document_endpoint_queues_youtube_transcripts(self):
        document = SimpleNamespace(
            id=9,
            processing_status="uploaded",
            source_type="youtube_video",
            source_url="https://www.youtube.com/watch?v=aaaaaaaaaaa",
            tenant_id="tenant-a",
        )
        db = FakeDatabaseService(document)

        with patch("app.routers.processing._ensure_youtube_transcript_queue_drainer"):
            response = asyncio.run(
                processing.process_document(
                    document_id=9,
                    background_tasks=BackgroundTasks(),
                    db=db,
                    neo4j=FakeNeo4jService(),
                )
            )

        self.assertEqual("YouTube transcript queued for document 9", response.message)
        self.assertEqual([], db.status_updates)
        self.assertIn(9, db.youtube_queue_entries)

    def test_cleanup_document_removes_processed_artifacts_and_calls_external_cleanup(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            document_dir = Path(temp_dir) / "processed" / "documents" / "42"
            outputs_dir = document_dir / "outputs"
            outputs_dir.mkdir(parents=True, exist_ok=True)
            url_source_path = Path(temp_dir) / "url_content" / "url_42.md"
            url_source_path.parent.mkdir(parents=True, exist_ok=True)
            url_source_path.write_text("# staged url content", encoding="utf-8")
            document_json_path = document_dir / "document.json"
            document_json_path.write_text("{}", encoding="utf-8")
            metadata_path = document_dir / "metadata.json"
            metadata_path.write_text(
                json.dumps(
                    {
                        "lightrag": {"staged_input_path": str(Path(temp_dir) / "inputs" / "000042-test.md")},
                        "source_path": str(url_source_path),
                    }
                ),
                encoding="utf-8",
            )

            document = SimpleNamespace(
                id=42,
                file_path="C:\\data\\uploads\\stored-file.pdf",
                processing_status="processed",
                filename="stored-file.pdf",
                original_filename="stored-file.pdf",
            )
            db = FakeDatabaseService(document)
            db.file_record = {
                "id": 42,
                "docling_document_path": str(document_json_path),
                "source_url": "https://example.com/article",
            }
            neo4j = FakeNeo4jService()
            lightrag = FakeLightRagHandoffService()

            response = asyncio.run(
                processing.cleanup_document(
                    document_id=42,
                    db=db,
                    neo4j=neo4j,
                    lightrag_handoff=lightrag,
                )
            )

            self.assertEqual("Cleanup completed for document 42", response.message)
            self.assertEqual([42], neo4j.deleted_document_ids)
            self.assertEqual([(42, str(Path(temp_dir) / "inputs" / "000042-test.md"))], lightrag.cleaned_documents)
            self.assertFalse(document_dir.exists())
            self.assertFalse(url_source_path.exists())

    def test_cleanup_document_rejects_processing_document(self):
        document = SimpleNamespace(
            id=11,
            file_path="C:\\data\\uploads\\queued.pdf",
            processing_status="processing",
            filename="queued.pdf",
            original_filename="queued.pdf",
        )
        db = FakeDatabaseService(document)
        db.file_record = {"id": 11, "docling_document_path": None}

        with self.assertRaises(HTTPException) as context:
            asyncio.run(
                processing.cleanup_document(
                    document_id=11,
                    db=db,
                    neo4j=FakeNeo4jService(),
                    lightrag_handoff=FakeLightRagHandoffService(),
                )
            )

        self.assertEqual(409, context.exception.status_code)
