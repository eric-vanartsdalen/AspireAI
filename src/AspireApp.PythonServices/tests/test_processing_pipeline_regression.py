from __future__ import annotations

import asyncio
import json
import sys
import tempfile
import unittest
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


class FakeDatabaseService:
    def __init__(self, document: SimpleNamespace | None = None):
        self.document = document
        self.status_updates: list[tuple[int, str, str | None]] = []
        self.ingestion_updates: list[dict] = []
        self.processing_updates: list[dict] = []
        self.saved_pages: list[dict] = []
        self.file_record: dict | None = None

    def get_document_by_id(self, document_id: int):
        if self.document and self.document.id == document_id:
            return self.document
        return None

    def update_file_status(self, file_id: int, status: str, error: str = None) -> None:
        self.status_updates.append((file_id, status, error))

    def resolve_upload_path(self, document):
        return document.file_path

    def update_file_processing_results(self, **kwargs) -> None:
        self.processing_updates.append(kwargs)

    def update_file_ingestion_metadata(self, **kwargs) -> None:
        self.ingestion_updates.append(kwargs)

    def save_document_page(self, **kwargs) -> None:
        self.saved_pages.append(kwargs)

    def list_unprocessed_documents(self):
        return [self.document] if self.document else []

    def get_processing_status(self, document_id: int):
        return None

    def get_file_by_id(self, file_id: int):
        if self.file_record and self.file_record.get("id") == file_id:
            return self.file_record
        return None


class FakeNeo4jService:
    def __init__(self):
        self.deleted_document_ids: list[int] = []
        self.created_documents: list[object] = []
        self.created_claims: list[dict] = []

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
        return [f"claim-node-{i}" for i in range(len(claims))]

    def delete_document_graph(self, document_id: int):
        self.deleted_document_ids.append(document_id)
        return {"deleted_documents": 1, "deleted_pages": 2}


class FakeLightRagHandoffService:
    def __init__(self):
        self.cleaned_documents: list[tuple[int, str | None]] = []

    def handoff_document(self, document, markdown_path):
        return {"scan_requested": True, "markdown_path": markdown_path}

    def cleanup_document(self, document, staged_input_path=None, delete_llm_cache=False, wait_timeout_seconds=30.0):
        self.cleaned_documents.append((document.id, staged_input_path))
        return {"doc_ids": ["doc_123"], "removed_paths": [staged_input_path] if staged_input_path else []}


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

    def test_cleanup_document_removes_processed_artifacts_and_calls_external_cleanup(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            document_dir = Path(temp_dir) / "processed" / "documents" / "42"
            outputs_dir = document_dir / "outputs"
            outputs_dir.mkdir(parents=True, exist_ok=True)
            document_json_path = document_dir / "document.json"
            document_json_path.write_text("{}", encoding="utf-8")
            metadata_path = document_dir / "metadata.json"
            metadata_path.write_text(
                json.dumps({"lightrag": {"staged_input_path": str(Path(temp_dir) / "inputs" / "000042-test.md")}}),
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
