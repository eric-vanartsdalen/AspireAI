from __future__ import annotations

import asyncio
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from fastapi import BackgroundTasks, HTTPException

from app.routers import processing


class FakeDatabaseService:
    def __init__(self, document: SimpleNamespace | None = None):
        self.document = document
        self.status_updates: list[tuple[int, str, str | None]] = []
        self.processing_updates: list[dict] = []
        self.saved_pages: list[dict] = []

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

    def save_document_page(self, **kwargs) -> None:
        self.saved_pages.append(kwargs)

    def list_unprocessed_documents(self):
        return [self.document] if self.document else []

    def get_processing_status(self, document_id: int):
        return None


class FakeNeo4jService:
    def create_document_node(self, document):
        return "doc-node"

    def create_page_nodes(self, pages, doc_node_id, document_id):
        return [f"page-node-{page.page_number}" for page in pages]

    def create_relationships(self, doc_node_id, page_node_ids):
        return None

    def create_sequential_relationships(self, page_node_ids):
        return None


class FakeLightRagHandoffService:
    def handoff_document(self, document, markdown_path):
        return {"scan_requested": True, "markdown_path": markdown_path}


class ProcessingPipelineRegressionTests(unittest.TestCase):
    def test_process_document_task_persists_pages_and_marks_processed(self):
        document = SimpleNamespace(
            id=42,
            file_path="C:\\data\\uploads\\stored-file.pdf",
            processing_status="uploaded",
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
                    SimpleNamespace(page_number=1, content="Page 1", metadata={"page": 1}),
                    SimpleNamespace(page_number=2, content="Page 2", metadata={"page": 2}),
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
        self.assertEqual(1, len(db.processing_updates))
        self.assertEqual(2, len(db.saved_pages))
        self.assertEqual("page-node-1", db.saved_pages[0]["neo4j_node_id"])
        self.assertEqual("Page 2", db.saved_pages[1]["content"])

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
