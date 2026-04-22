from __future__ import annotations

import asyncio
import os
import shutil
import sys
import unittest
from datetime import UTC, date, datetime, timedelta
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.routers import processing
from app.services import database_service as database_service_module
from app.services.database_service import DatabaseService
from app.services.url_handlers.base import FetchedContent

from fake_postgres import ATTEMPT_COLUMNS, FILE_COLUMNS, PAGE_COLUMNS, QUEUE_COLUMNS, FakeConnectionPool


class QueueStubDatabase:
    def __init__(self, document: SimpleNamespace):
        self.documents: dict[int, SimpleNamespace] = {}
        self.next_document_id = document.id + 1
        self.status_updates: list[tuple[int, str, str | None]] = []
        self.indexing_updates: list[tuple[int, str, str | None]] = []
        self.added_url_sources: list[dict[str, object]] = []
        self.enqueued_youtube_transcripts: list[dict[str, object]] = []
        self.ingestion_updates: list[dict[str, object]] = []
        self.processing_updates: list[dict[str, object]] = []
        self.saved_pages: list[dict[str, object]] = []
        self.register_document(document)

    def register_document(self, document: SimpleNamespace) -> None:
        if not hasattr(document, "indexing_status"):
            document.indexing_status = "not_requested"
        if not hasattr(document, "indexing_error"):
            document.indexing_error = None
        self.documents[document.id] = document

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
                    "status": getattr(document, "processing_status", "uploaded"),
                }
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

    def enqueue_youtube_transcript(self, *, file_id: int, source_url: str, tenant_id: str = "default") -> int:
        queue_id = len(self.enqueued_youtube_transcripts) + 1
        self.enqueued_youtube_transcripts.append(
            {
                "id": queue_id,
                "file_id": file_id,
                "source_url": source_url,
                "tenant_id": tenant_id,
            }
        )
        return queue_id

    def update_file_ingestion_metadata(self, **kwargs) -> None:
        self.ingestion_updates.append(kwargs)

    def update_file_processing_results(self, **kwargs) -> None:
        self.processing_updates.append(kwargs)

    def save_document_page(self, **kwargs) -> None:
        self.saved_pages.append(kwargs)


class MinimalNeo4jService:
    def create_document_node(self, document):
        return "doc-node"

    def create_page_nodes(self, pages, doc_node_id, document_id):
        return [f"page-node-{page.page_number}" for page in pages]

    def create_relationships(self, doc_node_id, page_node_ids):
        return None

    def create_sequential_relationships(self, page_node_ids):
        return None

    def create_claim_nodes(self, claims, page_node_id, document_id, page_number):
        return []


class UnavailableEmbeddingService:
    def is_available(self) -> bool:
        return False


class EmptyClaimExtractionService:
    def extract_claims(self, content: str, source_confidence: float, source_type: str):
        return []


class NoopLightRagHandoffService:
    def handoff_document(self, document, markdown_path):
        return {"scan_requested": False, "markdown_path": markdown_path}

    def wait_for_document_readiness(self, staged_input_path, *, status_callback=None, **_kwargs):
        if status_callback is not None:
            status_callback("not_requested", None)
        return {"indexing_status": "not_requested", "indexing_error": None}


class FakeQueueFetcher:
    channel_url = "https://www.youtube.com/@queue-testing/videos"
    child_urls = [
        "https://www.youtube.com/watch?v=aaaaaaaaaaa",
        "https://www.youtube.com/watch?v=bbbbbbbbbbb",
    ]

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
            metadata={"title": "Queue Testing", "source_type": "youtube_channel"},
            child_urls=list(self.child_urls),
        )

    def get_handler(self, url: str):
        if "watch?v=" in url:
            return SimpleNamespace(handler_name="youtube_video")
        return SimpleNamespace(handler_name="youtube_channel")


class YouTubeTranscriptQueueTests(unittest.TestCase):
    def setUp(self) -> None:
        FakeConnectionPool.reset()
        DatabaseService._pools.clear()
        self.pool_patch = patch.object(database_service_module, "ConnectionPool", FakeConnectionPool)
        self.pool_patch.start()
        self.scratch_root = TEST_ROOT / "_scratch_youtube_transcript_queue"
        shutil.rmtree(self.scratch_root, ignore_errors=True)

    def tearDown(self) -> None:
        self.pool_patch.stop()
        DatabaseService._pools.clear()
        FakeConnectionPool.reset()
        shutil.rmtree(self.scratch_root, ignore_errors=True)

    def _make_runtime_dir(self, name: str) -> Path:
        runtime_dir = self.scratch_root / name
        shutil.rmtree(runtime_dir, ignore_errors=True)
        runtime_dir.mkdir(parents=True, exist_ok=True)
        return runtime_dir

    def _create_service(self) -> DatabaseService:
        return DatabaseService("host=test port=5432 dbname=appdb user=postgres password=pw")

    def _get_file_row(self, service: DatabaseService, file_id: int) -> dict[str, object]:
        state = FakeConnectionPool.states[service.connection_string]
        return next(row for row in state.tables["files"]["rows"] if row["id"] == file_id)

    def _seed_file(
        self,
        service: DatabaseService,
        *,
        source_type: str,
        status: str,
        uploaded_at: datetime,
        processing_started_at: datetime | None = None,
        processing_completed_at: datetime | None = None,
    ) -> int:
        file_suffix = len(FakeConnectionPool.states[service.connection_string].tables["files"]["rows"]) + 1
        if source_type == "youtube_video":
            source_url = f"https://www.youtube.com/watch?v={file_suffix:011d}"
            mime_type = "text/plain"
        else:
            source_url = f"https://example.com/{source_type}/{file_suffix}"
            mime_type = "text/html"

        file_id = service.create_file_record(
            file_name=f"{source_type}-{file_suffix}",
            original_file_name=f"{source_type}-{file_suffix}",
            file_path="",
            mime_type=mime_type,
            uploaded_at=uploaded_at,
            status=status,
            tenant_id="tenant-a",
            source_type=source_type,
            source_url=source_url,
        )
        row = self._get_file_row(service, file_id)
        row["status"] = status
        row["uploaded_at"] = uploaded_at
        row["processing_started_at"] = processing_started_at
        row["processing_completed_at"] = processing_completed_at
        row["processing_error"] = None if status != "error" else "attempt failed"
        return file_id

    def _append_attempt(
        self,
        service: DatabaseService,
        *,
        attempted_at: datetime,
        attempted_on: date | None = None,
        file_id: int = 999,
        queue_id: int = 999,
    ) -> None:
        state = FakeConnectionPool.states[service.connection_string]
        state.tables["youtube_transcript_attempts"]["rows"].append(
            {
                "id": state.next_attempt_id,
                "queue_id": queue_id,
                "file_id": file_id,
                "attempted_at": attempted_at,
                "attempted_on": attempted_on or attempted_at.astimezone(UTC).date(),
            }
        )
        state.next_attempt_id += 1

    def test_collect_child_document_ids_adds_uploaded_youtube_rows(self):
        parent = SimpleNamespace(id=77, tenant_id="tenant-a")
        db = QueueStubDatabase(
            SimpleNamespace(
                id=77,
                filename="queue-testing",
                original_filename="queue-testing",
                file_path="",
                mime_type="text/html",
                processing_status="uploaded",
                tenant_id="tenant-a",
                source_type="youtube_channel",
                source_url=FakeQueueFetcher.channel_url,
            )
        )
        fetcher = FakeQueueFetcher()
        content = FetchedContent(
            text="",
            content_type="youtube_channel",
            metadata={"source_type": "youtube_channel"},
            child_urls=list(fetcher.child_urls),
        )

        child_document_ids = processing._collect_child_document_ids(parent, content, db, fetcher)

        self.assertEqual(2, len(child_document_ids))
        self.assertEqual(2, len(db.added_url_sources))
        self.assertEqual(
            ["youtube_video", "youtube_video"],
            [item["source_type"] for item in db.added_url_sources],
        )
        self.assertEqual(
            ["uploaded", "uploaded"],
            [item["status"] for item in db.added_url_sources],
        )

    def test_processing_status_records_attempt_timestamp(self):
        service = self._create_service()
        file_id = self._seed_file(
            service,
            source_type="youtube_video",
            status="uploaded",
            uploaded_at=datetime(2026, 4, 22, 12, 0, tzinfo=UTC),
        )

        service.update_file_status(file_id, "processing")
        record = service.get_file_by_id(file_id)

        self.assertEqual("processing", record["status"])
        self.assertIsNotNone(record["processing_started_at"])
        self.assertIsNone(record["processing_completed_at"])
        self.assertEqual("not_requested", record["indexing_status"])

    def test_child_youtube_urls_stay_enqueued_until_queue_worker_runs(self):
        """Channel expansion should enqueue transcript URLs without recursively processing them immediately."""
        parent = SimpleNamespace(
            id=77,
            filename="queue-testing-channel",
            original_filename="queue-testing-channel",
            file_path="",
            mime_type="text/html",
            processing_status="uploaded",
            tenant_id="tenant-a",
            source_type="youtube_channel",
            source_url=FakeQueueFetcher.channel_url,
        )
        db = QueueStubDatabase(parent)
        runtime_dir = self._make_runtime_dir("child-enqueue")
        fetcher = FakeQueueFetcher()
        processed_documents: list[int] = []

        def process_document(document, path):
            processed_documents.append(document.id)
            return (
                SimpleNamespace(
                    docling_document_path=f"/app/data/processed/documents/{document.id}/document.json",
                    total_pages=1,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": f"/app/data/processed/documents/{document.id}/output.md"},
                ),
                [SimpleNamespace(page_number=1, content=f"Processed {document.id}", metadata={"page": 1})],
            )

        with patch.dict(os.environ, {"ASPIRE_DATA_PATH": str(runtime_dir)}, clear=False), \
            patch("app.routers.processing.get_url_content_fetcher", return_value=fetcher), \
            patch("app.routers.processing.EmbeddingService", return_value=UnavailableEmbeddingService()), \
            patch("app.routers.processing.ClaimExtractionService", return_value=EmptyClaimExtractionService()), \
            patch("app.routers.processing._ensure_youtube_transcript_queue_drainer") as ensure_drainer:
            asyncio.run(
                processing.process_document_task(
                    document_id=77,
                    db=db,
                    docling=SimpleNamespace(process_document=process_document),
                    neo4j=MinimalNeo4jService(),
                    lightrag_handoff=NoopLightRagHandoffService(),
                )
            )

        queued_child_ids = [item["id"] for item in db.added_url_sources]
        self.assertEqual([77], processed_documents)
        self.assertEqual(queued_child_ids, [item["file_id"] for item in db.enqueued_youtube_transcripts])
        self.assertGreaterEqual(ensure_drainer.call_count, 1)
        for child_id in queued_child_ids:
            self.assertEqual("uploaded", db.documents[child_id].processing_status)
            self.assertEqual("not_requested", db.documents[child_id].indexing_status)
            self.assertNotIn((child_id, "processing", None), db.status_updates)
            self.assertNotIn((child_id, "ready", None), db.indexing_updates)

    def test_schema_compatibility_adds_indexing_columns_with_defaults(self):
        conninfo = "host=test port=5432 dbname=appdb user=postgres password=pw"
        state = FakeConnectionPool.states.setdefault(conninfo, FakeConnectionPool(conninfo).state)
        legacy_file_columns = [column for column in FILE_COLUMNS if column not in {"indexing_status", "indexing_error"}]
        state.tables["files"] = {
            "columns": legacy_file_columns,
            "rows": [
                {
                    "id": 1,
                    "file_name": "legacy.pdf",
                    "original_file_name": "legacy.pdf",
                    "file_path": "legacy.pdf",
                    "file_hash": "",
                    "file_size": 0,
                    "mime_type": "application/pdf",
                    "uploaded_at": datetime(2026, 4, 22, 12, 0, tzinfo=UTC),
                    "status": "uploaded",
                    "processing_started_at": None,
                    "processing_completed_at": None,
                    "processing_error": None,
                    "docling_document_path": None,
                    "total_pages": None,
                    "neo4j_document_node_id": None,
                    "tenant_id": "default",
                    "source_type": "upload",
                    "source_confidence": 0.7,
                    "source_url": None,
                }
            ],
        }
        state.tables["document_pages"] = {"columns": list(PAGE_COLUMNS), "rows": []}
        state.tables["youtube_transcript_queue"] = {"columns": list(QUEUE_COLUMNS), "rows": []}
        state.tables["youtube_transcript_attempts"] = {"columns": list(ATTEMPT_COLUMNS), "rows": []}

        service = DatabaseService(conninfo)
        row = self._get_file_row(service, 1)

        self.assertIn("indexing_status", state.tables["files"]["columns"])
        self.assertIn("indexing_error", state.tables["files"]["columns"])
        self.assertEqual("not_requested", row["indexing_status"])
        self.assertIsNone(row["indexing_error"])

    def test_processing_status_exposes_indexing_readiness(self):
        service = self._create_service()
        file_id = self._seed_file(
            service,
            source_type="upload",
            status="uploaded",
            uploaded_at=datetime(2026, 4, 22, 12, 0, tzinfo=UTC),
        )

        service.update_file_status(file_id, "processing")
        service.update_file_indexing_status(file_id, "indexing")
        in_progress_status = service.get_processing_status(file_id)
        in_progress_document = service.get_document_by_id(file_id)

        self.assertEqual("indexing", in_progress_status.indexing_status)
        self.assertFalse(in_progress_status.ready)
        self.assertEqual("indexing", in_progress_document.indexing_status)
        self.assertFalse(in_progress_document.ready)

        service.update_file_processing_results(
            file_id=file_id,
            docling_path="/app/data/processed/documents/1/document.json",
            total_pages=1,
            neo4j_node_id=None,
        )
        service.update_file_indexing_status(file_id, "ready")
        service.update_file_status(file_id, "processed")

        completed_status = service.get_processing_status(file_id)
        completed_document = service.get_document_by_id(file_id)

        self.assertEqual("ready", completed_status.indexing_status)
        self.assertTrue(completed_status.ready)
        self.assertEqual("ready", completed_document.indexing_status)
        self.assertTrue(completed_document.ready)

    def test_claim_next_youtube_transcript_respects_one_attempt_per_minute(self):
        """A transcript attempt inside the last UTC minute should block the next queued YouTube transcript."""
        service = self._create_service()
        now = datetime(2026, 4, 22, 12, 0, tzinfo=UTC)
        first_id = self._seed_file(
            service,
            source_type="youtube_video",
            status="uploaded",
            uploaded_at=now,
        )
        second_id = self._seed_file(
            service,
            source_type="youtube_video",
            status="uploaded",
            uploaded_at=now + timedelta(seconds=1),
        )
        service.enqueue_youtube_transcript(
            file_id=first_id,
            source_url=service.get_file_by_id(first_id)["source_url"],
            tenant_id="tenant-a",
        )
        service.enqueue_youtube_transcript(
            file_id=second_id,
            source_url=service.get_file_by_id(second_id)["source_url"],
            tenant_id="tenant-a",
        )

        first_claim = service.claim_next_youtube_transcript(now=now)
        service.mark_youtube_transcript_completed(first_id)

        self.assertIsNotNone(first_claim)
        self.assertEqual(first_id, first_claim["file_id"])
        self.assertIsNone(service.claim_next_youtube_transcript(now=now + timedelta(seconds=30)))
        self.assertAlmostEqual(
            30.0,
            service.get_youtube_transcript_queue_wait_seconds(now=now + timedelta(seconds=30)),
            delta=0.01,
        )

        second_claim = service.claim_next_youtube_transcript(now=now + timedelta(seconds=61))

        self.assertIsNotNone(second_claim)
        self.assertEqual(second_id, second_claim["file_id"])

    def test_queue_processing_stops_after_fifty_attempts_in_same_utc_day(self):
        """The queue should stop dispatching YouTube transcript attempts after fifty UTC-day attempts."""
        service = self._create_service()
        now = datetime(2026, 4, 22, 12, 0, tzinfo=UTC)
        pending_id = self._seed_file(
            service,
            source_type="youtube_video",
            status="uploaded",
            uploaded_at=now,
        )
        service.enqueue_youtube_transcript(
            file_id=pending_id,
            source_url=service.get_file_by_id(pending_id)["source_url"],
            tenant_id="tenant-a",
        )
        for attempt_number in range(50):
            self._append_attempt(
                service,
                attempted_at=now.replace(hour=0, minute=0) + timedelta(minutes=attempt_number),
                file_id=attempt_number + 1,
                queue_id=attempt_number + 1,
            )

        self.assertIsNone(service.claim_next_youtube_transcript(now=now))
        self.assertAlmostEqual(
            43200.0,
            service.get_youtube_transcript_queue_wait_seconds(now=now),
            delta=0.01,
        )

    def test_attempt_timestamps_and_utc_dates_drive_daily_limit(self):
        """Only attempt dates should consume today's quota; a prior-day attempt must not block today's fiftieth slot."""
        service = self._create_service()
        today = datetime(2026, 4, 22, 12, 0, tzinfo=UTC)
        first_pending_id = self._seed_file(
            service,
            source_type="youtube_video",
            status="uploaded",
            uploaded_at=today,
        )
        second_pending_id = self._seed_file(
            service,
            source_type="youtube_video",
            status="uploaded",
            uploaded_at=today + timedelta(seconds=1),
        )
        service.enqueue_youtube_transcript(
            file_id=first_pending_id,
            source_url=service.get_file_by_id(first_pending_id)["source_url"],
            tenant_id="tenant-a",
        )
        service.enqueue_youtube_transcript(
            file_id=second_pending_id,
            source_url=service.get_file_by_id(second_pending_id)["source_url"],
            tenant_id="tenant-a",
        )
        for attempt_number in range(49):
            self._append_attempt(
                service,
                attempted_at=today.replace(hour=0, minute=0) + timedelta(minutes=attempt_number),
                file_id=attempt_number + 1,
                queue_id=attempt_number + 1,
            )
        self._append_attempt(
            service,
            attempted_at=today - timedelta(days=1, minutes=1),
            attempted_on=(today - timedelta(days=1)).date(),
            file_id=500,
            queue_id=500,
        )

        first_claim = service.claim_next_youtube_transcript(now=today)
        service.mark_youtube_transcript_completed(first_claim["file_id"])

        self.assertIsNotNone(first_claim)
        self.assertEqual(first_pending_id, first_claim["file_id"])
        self.assertIsNone(service.claim_next_youtube_transcript(now=today + timedelta(minutes=1, seconds=1)))


if __name__ == "__main__":
    unittest.main()
