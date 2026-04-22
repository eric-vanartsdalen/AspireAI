from __future__ import annotations

import re
import sys
import tempfile
import unittest
from datetime import UTC, datetime, timedelta
from pathlib import Path
from unittest.mock import patch

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.services import database_service as database_service_module
from app.services.database_service import DatabaseService

from fake_postgres import FakeConnectionPool

REPO_ROOT = Path(__file__).resolve().parents[3]


class DatabaseContractAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        FakeConnectionPool.reset()
        DatabaseService._pools.clear()
        self.pool_patch = patch.object(database_service_module, "ConnectionPool", FakeConnectionPool)
        self.pool_patch.start()

    def tearDown(self) -> None:
        self.pool_patch.stop()
        DatabaseService._pools.clear()
        FakeConnectionPool.reset()

    def test_database_service_uses_postgres_environment_contract(self):
        with patch.dict(
            "os.environ",
            {
                "POSTGRES_HOST": "db-host",
                "POSTGRES_PORT": "5433",
                "POSTGRES_DATABASE": "documents",
                "POSTGRES_USER": "worker",
                "POSTGRES_PASSWORD": "secret",
            },
            clear=False,
        ):
            service = DatabaseService()

        self.assertEqual("environment", service.db_path_source)
        self.assertEqual("postgresql://db-host:5433/documents", service.db_path)

    def test_database_service_initializes_canonical_schema_and_indexes(self):
        service = DatabaseService("host=test port=5432 dbname=appdb user=postgres password=pw")
        schema = service.get_schema_snapshot()
        state = FakeConnectionPool.states[service.connection_string]

        self.assertEqual(
            {"files", "document_pages", "youtube_transcript_queue", "youtube_transcript_attempts"},
            set(schema["tables"]),
        )
        self.assertIn("file_hash", schema["columns"]["files"])
        self.assertIn("source_confidence", schema["columns"]["files"])
        self.assertIn("page_metadata", schema["columns"]["document_pages"])
        self.assertIn("last_attempted_at", schema["columns"]["youtube_transcript_queue"])
        self.assertIn("attempted_on", schema["columns"]["youtube_transcript_attempts"])
        self.assertTrue(
            {
                "idx_files_status",
                "idx_files_hash",
                "idx_files_uploaded",
                "idx_pages_file_id",
                "idx_pages_document_page",
                "idx_youtube_transcript_queue_file",
                "idx_youtube_transcript_queue_pending",
                "idx_youtube_transcript_attempts_date",
                "idx_youtube_transcript_attempts_queue",
            }.issubset(state.indexes)
        )

    def test_database_service_exposes_canonical_files_surface(self):
        service = DatabaseService("host=test port=5432 dbname=appdb user=postgres password=pw")
        file_id = service.create_file_record(
            file_name="stored-file.pdf",
            original_file_name="original.pdf",
            file_path=str(Path("data") / "uploads"),
            file_size=4,
            mime_type="application/pdf",
            status="uploaded",
            tenant_id="test-tenant",
        )

        service.update_file_status(file_id, "processing")
        service.update_file_processing_results(
            file_id=file_id,
            docling_path="/app/data/processed/documents/1/document.json",
            total_pages=3,
        )
        service.save_document_page(
            file_id=file_id,
            page_number=1,
            content="Persisted page content",
            metadata={"page_number": 1},
        )
        service.update_file_status(file_id, "processed")

        document = service.get_document_by_id(file_id)
        file_record = service.get_file_by_id(file_id)
        status = service.get_processing_status(file_id)
        pages = service.get_document_pages(file_id)
        health = service.health_check()

        self.assertEqual(file_id, document.id)
        self.assertEqual("test-tenant", file_record["tenant_id"])
        self.assertEqual(0.7, file_record["source_confidence"])
        self.assertEqual("processed", status.status)
        self.assertEqual(3, status.total_pages)
        self.assertEqual(1, status.processed_pages)
        self.assertEqual("Persisted page content", pages[0]["content"])
        self.assertEqual("healthy", health["status"])
        self.assertEqual("postgres", health["database_provider"])

    def test_create_file_record_derives_source_confidence_from_source_type(self):
        service = DatabaseService("host=test port=5432 dbname=appdb user=postgres password=pw")
        file_id = service.create_file_record(
            file_name="note.txt",
            original_file_name="note.txt",
            file_path="uploads",
            mime_type="text/plain",
            source_type="user_note",
        )

        file_record = service.get_file_by_id(file_id)

        self.assertEqual("user_note", file_record["source_type"])
        self.assertEqual(0.3, file_record["source_confidence"])

    def test_retryable_documents_include_error_rows_and_retry_clears_stale_artifacts(self):
        service = DatabaseService("host=test port=5432 dbname=appdb user=postgres password=pw")
        file_id = service.create_file_record(
            file_name="retry.pdf",
            original_file_name="retry.pdf",
            file_path="uploads",
            status="uploaded",
        )

        service.update_file_status(file_id, "processing")
        service.update_file_processing_results(
            file_id=file_id,
            docling_path="/app/data/processed/documents/retry/document.json",
            total_pages=2,
            neo4j_node_id="doc-node",
        )
        service.save_document_page(
            file_id=file_id,
            page_number=1,
            content="Old page",
            metadata={"attempt": 1},
            neo4j_node_id="page-node",
        )
        service.update_file_status(file_id, "error", "first attempt failed")

        retryable_ids = {document.id for document in service.list_unprocessed_documents()}
        self.assertIn(file_id, retryable_ids)

        service.update_file_status(file_id, "processing")
        record = service.get_file_by_id(file_id)
        pages = service.get_document_pages(file_id)

        self.assertEqual("processing", record["status"])
        self.assertIsNone(record["processing_completed_at"])
        self.assertIsNone(record["processing_error"])
        self.assertIsNone(record["docling_document_path"])
        self.assertIsNone(record["total_pages"])
        self.assertEqual([], pages)

    def test_youtube_transcript_queue_persists_attempt_limits_and_retry_state(self):
        service = DatabaseService("host=test port=5432 dbname=appdb user=postgres password=pw")
        file_id = service.add_url_datasource(
            source_name="queued-video",
            source_url="https://www.youtube.com/watch?v=aaaaaaaaaaa",
            source_type="youtube_video",
            tenant_id="tenant-a",
        )
        service.enqueue_youtube_transcript(
            file_id=file_id,
            source_url="https://www.youtube.com/watch?v=aaaaaaaaaaa",
            tenant_id="tenant-a",
        )

        retryable_ids = {document.id for document in service.list_unprocessed_documents()}
        self.assertNotIn(file_id, retryable_ids)

        base_time = datetime(2026, 4, 22, 12, 0, tzinfo=UTC)
        claimed = service.claim_next_youtube_transcript(now=base_time)
        self.assertIsNotNone(claimed)
        self.assertEqual(file_id, claimed["file_id"])
        self.assertGreaterEqual(service.get_youtube_transcript_queue_wait_seconds(now=base_time), 59.0)

        service.mark_youtube_transcript_failed(file_id, "rate limited")
        queue_entry = service.get_youtube_transcript_queue_entry(file_id)
        self.assertEqual("rate limited", queue_entry["last_error"])
        self.assertIsNone(queue_entry["completed_at"])

        current_time = base_time
        for attempt_index in range(1, service.YOUTUBE_TRANSCRIPT_DAILY_LIMIT):
            current_time += timedelta(minutes=1, seconds=1)
            claimed = service.claim_next_youtube_transcript(now=current_time)
            self.assertIsNotNone(claimed, f"Expected queued transcript on attempt {attempt_index + 1}")
            service.mark_youtube_transcript_failed(file_id, f"attempt {attempt_index + 1}")

        blocked_time = current_time + timedelta(minutes=1, seconds=1)
        self.assertIsNone(service.claim_next_youtube_transcript(now=blocked_time))
        self.assertGreater(service.get_youtube_transcript_queue_wait_seconds(now=blocked_time), 0.0)

        next_day_time = datetime(2026, 4, 23, 0, 1, tzinfo=UTC)
        claimed_next_day = service.claim_next_youtube_transcript(now=next_day_time)
        self.assertIsNotNone(claimed_next_day)
        self.assertEqual(file_id, claimed_next_day["file_id"])

        service.mark_youtube_transcript_completed(file_id)
        queue_entry = service.get_youtube_transcript_queue_entry(file_id)
        self.assertIsNotNone(queue_entry["completed_at"])

    def test_resolve_upload_path_maps_shared_data_roots(self):
        service = DatabaseService("host=test port=5432 dbname=appdb user=postgres password=pw")
        with tempfile.TemporaryDirectory() as temp_dir:
            runtime_root = Path(temp_dir)
            expected_file = runtime_root / "uploads" / "invoice.pdf"
            expected_file.parent.mkdir(parents=True, exist_ok=True)
            expected_file.write_text("pdf", encoding="utf-8")
            service._runtime_data_roots = [runtime_root]

            resolved = service.resolve_upload_path(
                {
                    "file_path": r"C:\repo\AspireAI\data\uploads",
                    "file_name": "invoice.pdf",
                }
            )

        self.assertEqual(expected_file.resolve(), resolved)


class SharedUploadContractAuditTests(unittest.TestCase):
    def test_web_file_metadata_columns_match_python_projection(self):
        web_entities_source = (
            REPO_ROOT / "src" / "AspireApp.Web" / "Data" / "DocumentEntities.cs"
        ).read_text(encoding="utf-8")
        database_service_source = (
            REPO_ROOT / "src" / "AspireApp.PythonServices" / "app" / "services" / "database_service.py"
        ).read_text(encoding="utf-8")

        shared_columns = {
            "id",
            "file_name",
            "original_file_name",
            "file_path",
            "file_hash",
            "file_size",
            "mime_type",
            "uploaded_at",
            "status",
            "processing_started_at",
            "processing_completed_at",
            "processing_error",
            "docling_document_path",
            "total_pages",
            "neo4j_document_node_id",
            "source_type",
            "source_url",
        }

        for column in shared_columns:
            self.assertIn(f'[Column("{column}")]', web_entities_source)
            self.assertIn(column, database_service_source)

        self.assertIn('filename=file_dict["file_name"]', database_service_source)
        self.assertIn('original_filename=file_dict["original_file_name"]', database_service_source)
        self.assertIn("processing_status=status", database_service_source)
        self.assertIn('processed=(status == "processed")', database_service_source)

    def test_web_document_page_columns_match_python_page_projection(self):
        web_entities_source = (
            REPO_ROOT / "src" / "AspireApp.Web" / "Data" / "DocumentEntities.cs"
        ).read_text(encoding="utf-8")
        database_service_source = (
            REPO_ROOT / "src" / "AspireApp.PythonServices" / "app" / "services" / "database_service.py"
        ).read_text(encoding="utf-8")

        page_columns = {
            "id",
            "file_id",
            "page_number",
            "content",
            "page_metadata",
            "neo4j_page_node_id",
        }

        for column in page_columns:
            self.assertIn(f'[Column("{column}")]', web_entities_source)
            self.assertIn(column, database_service_source)

    def test_apphost_and_runtime_align_on_postgres_upload_store(self):
        app_host_source = (
            REPO_ROOT / "src" / "AspireApp.AppHost" / "AppHost.cs"
        ).read_text(encoding="utf-8")
        database_service_source = (
            REPO_ROOT / "src" / "AspireApp.PythonServices" / "app" / "services" / "database_service.py"
        ).read_text(encoding="utf-8")
        program_source = (
            REPO_ROOT / "src" / "AspireApp.Web" / "Program.cs"
        ).read_text(encoding="utf-8")
        file_storage_source = (
            REPO_ROOT / "src" / "AspireApp.Web" / "Shared" / "FileStorageService.cs"
        ).read_text(encoding="utf-8")
        requirements_source = (
            REPO_ROOT / "src" / "AspireApp.PythonServices" / "requirements.txt"
        ).read_text(encoding="utf-8")

        upload_store_match = re.search(
            r'var uploadStore = postgres\.AddDatabase\("(?P<database>[^"]+)"\);',
            app_host_source,
        )
        self.assertIsNotNone(upload_store_match, "AppHost should register a named Postgres upload store")
        upload_store_name = upload_store_match.group("database")

        self.assertIn('.WithReference(uploadStore)', app_host_source)
        self.assertIn(
            f'.WithEnvironment("POSTGRES_DATABASE", "{upload_store_name}")',
            app_host_source,
        )
        self.assertIn(f'GetConnectionString("{upload_store_name}")', program_source)
        self.assertIn("options.UseNpgsql(connectionString)", program_source)
        self.assertNotIn("UseSqlite", program_source)
        self.assertNotIn("SqliteConnection", file_storage_source)
        self.assertNotIn("wal_checkpoint", file_storage_source)
        self.assertIn("psycopg-pool", requirements_source)
        self.assertIn("PsycopgConnectionPool", database_service_source)
