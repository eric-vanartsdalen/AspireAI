import gc
import json
import os
import shutil
import sqlite3
import sys
import tempfile
import types
import unittest
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from threading import Thread
from types import SimpleNamespace
from unittest.mock import MagicMock, patch
from uuid import uuid4


PROJECT_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = Path(__file__).resolve().parents[3]
SCRATCH_ROOT = Path(__file__).resolve().parent / "_scratch_processing_pipeline"


def _purge_modules(*prefixes: str) -> None:
    for module_name in list(sys.modules):
        if any(
            module_name == prefix or module_name.startswith(f"{prefix}.")
            for prefix in prefixes
        ):
            sys.modules.pop(module_name, None)


def _install_fake_pydantic() -> None:
    module = types.ModuleType("pydantic")

    class BaseModel:
        def __init__(self, **kwargs):
            annotations = {}
            for cls in reversed(self.__class__.__mro__):
                annotations.update(getattr(cls, "__annotations__", {}))

            for field_name in annotations:
                if field_name in kwargs:
                    value = kwargs[field_name]
                elif hasattr(self.__class__, field_name):
                    value = getattr(self.__class__, field_name)
                else:
                    value = None
                setattr(self, field_name, value)

            for key, value in kwargs.items():
                setattr(self, key, value)

        def dict(self):
            return self.__dict__.copy()

    module.BaseModel = BaseModel
    sys.modules["pydantic"] = module


def _install_fake_fastapi() -> None:
    module = types.ModuleType("fastapi")

    class APIRouter:
        def __init__(self, *args, **kwargs):
            pass

        def post(self, *args, **kwargs):
            def decorator(func):
                return func

            return decorator

        def get(self, *args, **kwargs):
            def decorator(func):
                return func

            return decorator

    class HTTPException(Exception):
        def __init__(self, status_code: int, detail: str):
            super().__init__(detail)
            self.status_code = status_code
            self.detail = detail

    class BackgroundTasks:
        def add_task(self, *args, **kwargs):
            return None

    def Depends(dependency):
        return dependency

    def Query(default=None, **kwargs):
        return default

    module.APIRouter = APIRouter
    module.HTTPException = HTTPException
    module.Depends = Depends
    module.BackgroundTasks = BackgroundTasks
    module.Query = Query
    sys.modules["fastapi"] = module


def _install_processing_stubs() -> None:
    service_factory_module = types.ModuleType("app.services.service_factory")
    service_factory_module.get_docling_service = lambda: None

    neo4j_module = types.ModuleType("app.services.neo4j_service")

    class Neo4jService:
        pass

    neo4j_module.Neo4jService = Neo4jService

    sys.modules["app.services.service_factory"] = service_factory_module
    sys.modules["app.services.neo4j_service"] = neo4j_module


@contextmanager
def _patched_database_dependencies():
    original_modules = {"pydantic": sys.modules.get("pydantic")}

    _purge_modules("app")
    _install_fake_pydantic()
    sys.path.insert(0, str(PROJECT_ROOT))

    try:
        yield
    finally:
        sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
        _purge_modules("app")
        original_module = original_modules["pydantic"]
        if original_module is None:
            sys.modules.pop("pydantic", None)
        else:
            sys.modules["pydantic"] = original_module


@contextmanager
def _patched_processing_dependencies():
    original_modules = {
        name: sys.modules.get(name)
        for name in [
            "pydantic",
            "fastapi",
            "app.services.service_factory",
            "app.services.neo4j_service",
        ]
    }

    _purge_modules("app", "fastapi")
    _install_fake_pydantic()
    _install_fake_fastapi()
    _install_processing_stubs()
    sys.path.insert(0, str(PROJECT_ROOT))

    try:
        yield
    finally:
        sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
        _purge_modules("app", "fastapi")
        for name, module in original_modules.items():
            if module is None:
                sys.modules.pop(name, None)
            else:
                sys.modules[name] = module


def _close_database_pools(database_service_type) -> None:
    for pool in database_service_type._pools.values():
        pool.close_all()
    database_service_type._pools.clear()


@contextmanager
def _database_service_sandbox(case_name: str):
    with _patched_database_dependencies():
        from app.services.database_service import DatabaseService

        scratch_dir = SCRATCH_ROOT / f"{case_name}_{uuid4().hex}"
        uploads_dir = scratch_dir / "uploads"
        uploads_dir.mkdir(parents=True, exist_ok=True)
        db_path = scratch_dir / "data-resources.db"

        previous_db_path = os.environ.get("ASPIRE_DB_PATH")
        os.environ["ASPIRE_DB_PATH"] = str(db_path)

        try:
            _close_database_pools(DatabaseService)
            yield DatabaseService(), uploads_dir
        finally:
            _close_database_pools(DatabaseService)
            gc.collect()
            if previous_db_path is None:
                os.environ.pop("ASPIRE_DB_PATH", None)
            else:
                os.environ["ASPIRE_DB_PATH"] = previous_db_path
            shutil.rmtree(scratch_dir, ignore_errors=True)
            if SCRATCH_ROOT.exists() and not any(SCRATCH_ROOT.iterdir()):
                SCRATCH_ROOT.rmdir()


class ProcessingStatusLifecycleTests(unittest.TestCase):
    def test_status_lifecycle_uses_canonical_values_and_tracks_timestamps(self):
        with _database_service_sandbox("canonical_status_lifecycle") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="stored-file.pdf",
                original_file_name="original.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="Uploaded",
            )

            initial_status = service.get_processing_status(file_id)
            self.assertEqual("uploaded", initial_status.status)
            self.assertIsNone(initial_status.completed_at)
            self.assertIsNone(initial_status.error_message)

            service.update_file_status(file_id, "processing")
            processing_status = service.get_processing_status(file_id)
            self.assertEqual("processing", processing_status.status)
            self.assertIsNotNone(processing_status.started_at)
            self.assertIsNone(processing_status.completed_at)
            self.assertIsNone(processing_status.error_message)

            service.update_file_processing_results(
                file_id=file_id,
                docling_path="/app/data/processed/documents/1/document.json",
                total_pages=2,
            )
            service.update_file_status(file_id, "processed")

            processed_status = service.get_processing_status(file_id)
            self.assertEqual("processed", processed_status.status)
            self.assertEqual(2, processed_status.total_pages)
            self.assertIsNotNone(processed_status.started_at)
            self.assertIsNotNone(processed_status.completed_at)
            self.assertIsNone(processed_status.error_message)

    def test_failed_records_are_retry_eligible_and_retry_clears_stale_failure_state(self):
        with _database_service_sandbox("retry_eligibility") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="retry-file.pdf",
                original_file_name="retry-file.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="uploaded",
            )

            service.update_file_status(file_id, "processing")
            service.update_file_status(file_id, "error", "docling exploded")

            failed_status = service.get_processing_status(file_id)
            self.assertEqual("error", failed_status.status)
            self.assertEqual("docling exploded", failed_status.error_message)
            self.assertIsNotNone(failed_status.started_at)
            self.assertIsNotNone(failed_status.completed_at)

            retryable_ids = {document.id for document in service.list_unprocessed_documents()}
            self.assertIn(file_id, retryable_ids)

            service.update_file_status(file_id, "processing")
            retrying_status = service.get_processing_status(file_id)
            self.assertEqual("processing", retrying_status.status)
            self.assertIsNotNone(retrying_status.started_at)
            self.assertIsNone(retrying_status.completed_at)
            self.assertIsNone(retrying_status.error_message)

    def test_get_document_by_id_falls_back_to_fresh_connection_when_pooled_lookup_misses_row(self):
        with _database_service_sandbox("fresh_connection_fallback") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="fresh-file.pdf",
                original_file_name="fresh-file.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="uploaded",
            )

            original_get_connection = service._pool.get_connection

            class StaleCursor:
                def execute(self, *args, **kwargs):
                    return None

                def fetchone(self):
                    return None

            class StaleConnection:
                def cursor(self):
                    return StaleCursor()

            @contextmanager
            def stale_get_connection():
                yield StaleConnection()

            service._pool.get_connection = stale_get_connection
            try:
                document = service.get_document_by_id(file_id)
            finally:
                service._pool.get_connection = original_get_connection

            self.assertIsNotNone(document)
            self.assertEqual(file_id, document.id)
            self.assertEqual("uploaded", document.processing_status)

    def test_get_document_by_id_falls_back_to_full_scan_when_targeted_lookup_misses(self):
        with _database_service_sandbox("full_scan_fallback") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="scan-file.pdf",
                original_file_name="scan-file.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="uploaded",
            )

            expected_file = service.get_all_files()[0]
            original_fetch_file_row = service._fetch_file_row
            original_fetch_fresh = service._fetch_file_row_from_fresh_connection
            original_get_all_files = service.get_all_files

            service._fetch_file_row = lambda conn, lookup_id: None
            service._fetch_file_row_from_fresh_connection = lambda lookup_id: None
            service.get_all_files = lambda: [expected_file]
            try:
                document = service.get_document_by_id(file_id)
            finally:
                service._fetch_file_row = original_fetch_file_row
                service._fetch_file_row_from_fresh_connection = original_fetch_fresh
                service.get_all_files = original_get_all_files

            self.assertIsNotNone(document)
            self.assertEqual(file_id, document.id)
            self.assertEqual("uploaded", document.processing_status)

    def test_get_document_by_id_prefers_fresh_reads_for_shared_container_database(self):
        with _database_service_sandbox("fresh_reads_for_shared_container_db") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="shared-file.pdf",
                original_file_name="shared-file.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="uploaded",
            )

            service.update_file_status(file_id, "processed")
            fresh_row = service._fetch_file_row_from_fresh_connection(file_id)
            stale_row = list(fresh_row)
            stale_row[8] = "uploaded"

            original_get_connection = service._pool.get_connection
            original_should_prefer_fresh_reads = service._should_prefer_fresh_reads

            class StaleCursor:
                def execute(self, *args, **kwargs):
                    return None

                def fetchone(self):
                    return tuple(stale_row)

            class StaleConnection:
                def cursor(self):
                    return StaleCursor()

            @contextmanager
            def stale_get_connection():
                yield StaleConnection()

            service._pool.get_connection = stale_get_connection
            service._should_prefer_fresh_reads = lambda: True
            try:
                document = service.get_document_by_id(file_id)
            finally:
                service._pool.get_connection = original_get_connection
                service._should_prefer_fresh_reads = original_should_prefer_fresh_reads

            self.assertIsNotNone(document)
            self.assertEqual(file_id, document.id)
            self.assertEqual("processed", document.processing_status)

    def test_update_file_status_prefers_fresh_connection_for_shared_container_database(self):
        with _database_service_sandbox("fresh_writes_for_shared_container_db") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="shared-write-file.pdf",
                original_file_name="shared-write-file.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="uploaded",
            )

            original_get_connection = service._pool.get_connection
            original_should_prefer_fresh_reads = service._should_prefer_fresh_reads

            @contextmanager
            def fail_if_pool_used():
                raise AssertionError("Shared mounted file updates should bypass the pooled connection.")
                yield

            service._pool.get_connection = fail_if_pool_used
            service._should_prefer_fresh_reads = lambda: True
            try:
                service.update_file_status(file_id, "processing")
                status = service.get_processing_status(file_id)
            finally:
                service._pool.get_connection = original_get_connection
                service._should_prefer_fresh_reads = original_should_prefer_fresh_reads

            self.assertEqual("processing", status.status)

    def test_list_documents_falls_back_to_fresh_connection_when_pooled_lookup_misses_rows(self):
        with _database_service_sandbox("list_documents_fresh_connection_fallback") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="list-file.pdf",
                original_file_name="list-file.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="uploaded",
            )

            original_get_connection = service._pool.get_connection

            class StaleCursor:
                def execute(self, *args, **kwargs):
                    return None

                def fetchall(self):
                    return []

            class StaleConnection:
                def cursor(self):
                    return StaleCursor()

            @contextmanager
            def stale_get_connection():
                yield StaleConnection()

            service._pool.get_connection = stale_get_connection
            try:
                documents = service.list_documents()
            finally:
                service._pool.get_connection = original_get_connection

            self.assertEqual([file_id], [document.id for document in documents])
            self.assertEqual("uploaded", documents[0].processing_status)

    def test_list_unprocessed_documents_falls_back_to_fresh_connection_when_pooled_lookup_misses_rows(self):
        with _database_service_sandbox("list_unprocessed_fresh_connection_fallback") as (service, uploads_dir):
            file_id = service.create_file_record(
                file_name="retry-file.pdf",
                original_file_name="retry-file.pdf",
                file_path=str(uploads_dir),
                file_size=4,
                mime_type="application/pdf",
                status="uploaded",
            )

            original_get_connection = service._pool.get_connection

            class StaleCursor:
                def execute(self, *args, **kwargs):
                    return None

                def fetchall(self):
                    return []

            class StaleConnection:
                def cursor(self):
                    return StaleCursor()

            @contextmanager
            def stale_get_connection():
                yield StaleConnection()

            service._pool.get_connection = stale_get_connection
            try:
                documents = service.list_unprocessed_documents()
            finally:
                service._pool.get_connection = original_get_connection

            self.assertEqual([file_id], [document.id for document in documents])
            self.assertEqual("uploaded", documents[0].processing_status)


class DocumentVisibilityEndpointRegressionTests(unittest.IsolatedAsyncioTestCase):
    async def test_get_document_returns_document_when_pooled_lookup_misses_visible_row(self):
        with _patched_processing_dependencies():
            from app.routers.documents import get_document
            from app.services.database_service import DatabaseService

            scratch_dir = SCRATCH_ROOT / f"document_visibility_{uuid4().hex}"
            uploads_dir = scratch_dir / "uploads"
            uploads_dir.mkdir(parents=True, exist_ok=True)
            db_path = scratch_dir / "data-resources.db"

            previous_db_path = os.environ.get("ASPIRE_DB_PATH")
            os.environ["ASPIRE_DB_PATH"] = str(db_path)

            try:
                _close_database_pools(DatabaseService)
                service = DatabaseService()
                file_id = service.create_file_record(
                    file_name="visible-file.pdf",
                    original_file_name="visible-file.pdf",
                    file_path=str(uploads_dir),
                    file_size=4,
                    mime_type="application/pdf",
                    status="uploaded",
                )

                original_get_connection = service._pool.get_connection

                class StaleCursor:
                    def execute(self, *args, **kwargs):
                        return None

                    def fetchone(self):
                        return None

                class StaleConnection:
                    def cursor(self):
                        return StaleCursor()

                @contextmanager
                def stale_get_connection():
                    yield StaleConnection()

                service._pool.get_connection = stale_get_connection
                try:
                    document = await get_document(file_id, db=service)
                finally:
                    service._pool.get_connection = original_get_connection
            finally:
                _close_database_pools(DatabaseService)
                gc.collect()
                if previous_db_path is None:
                    os.environ.pop("ASPIRE_DB_PATH", None)
                else:
                    os.environ["ASPIRE_DB_PATH"] = previous_db_path
                shutil.rmtree(scratch_dir, ignore_errors=True)
                if SCRATCH_ROOT.exists() and not any(SCRATCH_ROOT.iterdir()):
                    SCRATCH_ROOT.rmdir()

            self.assertEqual(file_id, document.id)
            self.assertEqual("visible-file.pdf", document.filename)
            self.assertEqual("uploaded", document.processing_status)

    async def test_get_document_returns_not_found_for_missing_document(self):
        with _patched_processing_dependencies():
            from app.routers.documents import get_document
            from fastapi import HTTPException

            missing_db = SimpleNamespace(get_document_by_id=lambda document_id: None)

            with self.assertRaises(HTTPException) as missing_error:
                await get_document(404, db=missing_db)

            self.assertEqual(404, missing_error.exception.status_code)
            self.assertEqual("Document not found", missing_error.exception.detail)

    async def test_get_document_uses_fallback_when_env_path_is_unusable(self):
        with _patched_processing_dependencies():
            from app.routers.documents import get_document
            import app.services.database_service as database_service_module

            DatabaseService = database_service_module.DatabaseService

            scratch_dir = SCRATCH_ROOT / f"document_visibility_fallback_{uuid4().hex}"
            repo_root = scratch_dir / "repo"
            env_root = scratch_dir / "env"
            uploads_dir = repo_root / "data" / "uploads"
            uploads_dir.mkdir(parents=True, exist_ok=True)
            repo_db = repo_root / "database" / "data-resources.db"
            repo_db.parent.mkdir(parents=True, exist_ok=True)
            env_db = env_root / "database" / "data-resources.db"
            env_db.parent.mkdir(parents=True, exist_ok=True)

            previous_db_path = os.environ.get("ASPIRE_DB_PATH")
            os.environ["ASPIRE_DB_PATH"] = str(env_db)
            service = None
            file_id = None
            document = None
            db_path = None
            db_path_source = None
            try:
                _close_database_pools(DatabaseService)
                original_create_connection = database_service_module.ConnectionPool._create_connection

                def failing_create_connection(pool_self):
                    if pool_self.db_path == str(env_db):
                        raise sqlite3.OperationalError("disk I/O error")
                    return original_create_connection(pool_self)

                with (
                    patch.object(
                        database_service_module.ConnectionPool,
                        "_create_connection",
                        new=failing_create_connection,
                    ),
                    patch.object(DatabaseService, "_get_repository_root", return_value=repo_root),
                    patch.object(DatabaseService, "_is_running_in_container", return_value=False),
                    patch.object(database_service_module.Path, "cwd", return_value=repo_root),
                ):
                    service = DatabaseService()
                    file_id = service.create_file_record(
                        file_name="fallback-file.pdf",
                        original_file_name="fallback-file.pdf",
                        file_path=str(uploads_dir),
                        file_size=4,
                        mime_type="application/pdf",
                        status="uploaded",
                    )
                    document = await get_document(file_id, db=service)
                    db_path_source = service.db_path_source
                    db_path = service.db_path
            finally:
                _close_database_pools(DatabaseService)
                gc.collect()
                if previous_db_path is None:
                    os.environ.pop("ASPIRE_DB_PATH", None)
                else:
                    os.environ["ASPIRE_DB_PATH"] = previous_db_path
                shutil.rmtree(scratch_dir, ignore_errors=True)
                if SCRATCH_ROOT.exists() and not any(SCRATCH_ROOT.iterdir()):
                    SCRATCH_ROOT.rmdir()

            self.assertIsNotNone(service)
            self.assertEqual(str(repo_db), db_path)
            self.assertEqual("repository", db_path_source)
            self.assertIsNotNone(file_id)
            self.assertIsNotNone(document)
            self.assertEqual(file_id, document.id)
            self.assertEqual("fallback-file.pdf", document.filename)
            self.assertEqual("uploaded", document.processing_status)


class ConnectionPoolJournalModeTests(unittest.TestCase):
    def test_docs_mounted_database_prefers_delete_journal_in_container(self):
        with _patched_database_dependencies():
            import app.services.database_service as database_service_module

            mock_connection = MagicMock()
            mock_connection.execute.return_value = None

            with (
                patch.object(
                    database_service_module.sqlite3,
                    "connect",
                    return_value=mock_connection,
                ),
                patch.object(
                    database_service_module.ConnectionPool,
                    "_apply_pragma",
                ) as apply_pragma,
            ):
                pool = database_service_module.ConnectionPool(
                    "/app/docs-database/data-resources.db",
                    prefer_delete_journal=True,
                )

                result = pool._create_connection()

            self.assertIs(result, mock_connection)
            self.assertGreaterEqual(len(apply_pragma.call_args_list), 1)
            self.assertEqual(
                "PRAGMA journal_mode=DELETE",
                apply_pragma.call_args_list[0].args[1],
            )


class FakeDatabase:
    def __init__(self, document, resolved_path: Path):
        self.document = document
        self.resolved_path = resolved_path
        self.status_updates = []
        self.resolve_calls = []
        self.processing_results = []
        self.saved_pages = []

    def update_file_status(self, file_id: int, status: str, error: str = None) -> None:
        self.status_updates.append((file_id, status, error))

    def get_document_by_id(self, document_id: int):
        return self.document if document_id == self.document.id else None

    def resolve_upload_path(self, document):
        self.resolve_calls.append(document)
        return self.resolved_path

    def update_file_processing_results(self, **kwargs) -> None:
        self.processing_results.append(kwargs)

    def save_document_page(self, **kwargs) -> None:
        self.saved_pages.append(kwargs)


class FakeNeo4j:
    def __init__(self):
        self.document_nodes = []
        self.page_batches = []
        self.relationships = []
        self.sequential_relationships = []

    def create_document_node(self, document):
        self.document_nodes.append(document)
        return "doc-node-1"

    def create_page_nodes(self, pages, doc_node_id, document_id):
        self.page_batches.append((pages, doc_node_id, document_id))
        return [f"page-node-{index}" for index, _ in enumerate(pages, start=1)]

    def create_relationships(self, doc_node_id, page_node_ids):
        self.relationships.append((doc_node_id, list(page_node_ids)))

    def create_sequential_relationships(self, page_node_ids):
        self.sequential_relationships.append(list(page_node_ids))


class FakeDocling:
    def __init__(self, processed_doc, pages, error: Exception = None):
        self.processed_doc = processed_doc
        self.pages = pages
        self.error = error
        self.calls = []

    def process_document(self, document, resolved_file_path):
        self.calls.append((document, resolved_file_path))
        if self.error is not None:
            raise self.error
        return self.processed_doc, self.pages


class FakeLightRagHandoff:
    def __init__(self, error: Exception = None):
        self.error = error
        self.calls = []

    def handoff_document(self, document, markdown_path):
        self.calls.append((document, markdown_path))
        if self.error is not None:
            raise self.error
        return {
            "staged_input_path": f"/app/data/inputs/{document.id:06d}.md",
            "scan_requested": True,
            "scan_response": {
                "status": "scanning_started",
                "track_id": f"scan-{document.id}",
            },
        }


class RecordingBackgroundTasks:
    def __init__(self):
        self.calls = []

    def add_task(self, func, *args, **kwargs):
        self.calls.append(
            {
                "func": func,
                "args": args,
                "kwargs": kwargs,
            }
        )


class ProcessDocumentEndpointContractTests(unittest.IsolatedAsyncioTestCase):
    async def test_process_document_marks_processing_before_background_task_runs(self):
        with _patched_processing_dependencies():
            from app.routers.processing import process_document, process_document_task

            document = SimpleNamespace(id=55, filename="queued-file.pdf", processing_status="uploaded")
            db = FakeDatabase(document=document, resolved_path=Path("C:\\app\\data\\uploads\\queued-file.pdf"))
            background_tasks = RecordingBackgroundTasks()
            neo4j = FakeNeo4j()

            response = await process_document(55, background_tasks, db, neo4j)

            self.assertEqual("Processing started for document 55", response.message)
            self.assertEqual([(55, "processing", None)], db.status_updates)
            self.assertEqual(1, len(background_tasks.calls))
            self.assertIs(process_document_task, background_tasks.calls[0]["func"])
            self.assertEqual((55, db, None, neo4j), background_tasks.calls[0]["args"])
            self.assertEqual({"mark_processing_started": False}, background_tasks.calls[0]["kwargs"])

    async def test_process_document_uses_status_codes_for_missing_or_duplicate_work(self):
        with _patched_processing_dependencies():
            from app.routers.processing import process_document
            from fastapi import HTTPException

            missing_db = FakeDatabase(
                document=SimpleNamespace(id=1, filename="other.pdf", processing_status="uploaded"),
                resolved_path=Path("C:\\app\\data\\uploads\\other.pdf"),
            )
            background_tasks = RecordingBackgroundTasks()

            with self.assertRaises(HTTPException) as missing_error:
                await process_document(404, background_tasks, missing_db, FakeNeo4j())
            self.assertEqual(404, missing_error.exception.status_code)

            processed_db = FakeDatabase(
                document=SimpleNamespace(id=77, filename="done.pdf", processing_status="processed"),
                resolved_path=Path("C:\\app\\data\\uploads\\done.pdf"),
            )
            with self.assertRaises(HTTPException) as processed_error:
                await process_document(77, background_tasks, processed_db, FakeNeo4j())
            self.assertEqual(400, processed_error.exception.status_code)

            in_progress_db = FakeDatabase(
                document=SimpleNamespace(id=88, filename="busy.pdf", processing_status="processing"),
                resolved_path=Path("C:\\app\\data\\uploads\\busy.pdf"),
            )
            with self.assertRaises(HTTPException) as in_progress_error:
                await process_document(88, background_tasks, in_progress_db, FakeNeo4j())
            self.assertEqual(409, in_progress_error.exception.status_code)


class ProcessDocumentTaskRegressionTests(unittest.IsolatedAsyncioTestCase):
    async def test_process_document_task_marks_processed_and_persists_pages(self):
        with _patched_processing_dependencies():
            from app.routers.processing import process_document_task

            with tempfile.TemporaryDirectory() as temp_dir:
                doc_dir = Path(temp_dir) / "processed" / "documents" / "41"
                outputs_dir = doc_dir / "outputs"
                outputs_dir.mkdir(parents=True, exist_ok=True)
                document_json_path = doc_dir / "document.json"
                document_json_path.write_text("{}", encoding="utf-8")
                markdown_path = outputs_dir / "stored-file.md"
                markdown_path.write_text("# Stored file", encoding="utf-8")

                document = SimpleNamespace(id=41, filename="stored-file.pdf")
                processed_doc = SimpleNamespace(
                    docling_document_path=str(document_json_path),
                    total_pages=2,
                    neo4j_node_id=None,
                    processing_metadata={"markdown_path": str(markdown_path)},
                )
                pages = [
                    SimpleNamespace(page_number=1, content="Page one", metadata={"section": "intro"}),
                    SimpleNamespace(page_number=2, content="Page two", metadata={"section": "body"}),
                ]
                db = FakeDatabase(document=document, resolved_path=Path("C:\\app\\data\\uploads\\stored-file.pdf"))
                docling = FakeDocling(processed_doc=processed_doc, pages=pages)
                neo4j = FakeNeo4j()
                lightrag_handoff = FakeLightRagHandoff()

                await process_document_task(41, db, docling, neo4j, lightrag_handoff)

                metadata_path = doc_dir / "metadata.json"
                metadata = json.loads(metadata_path.read_text(encoding="utf-8"))

            self.assertEqual(
                [(41, "processing", None), (41, "processed", None)],
                db.status_updates,
            )
            self.assertEqual([document], db.resolve_calls)
            self.assertEqual([(document, db.resolved_path)], docling.calls)
            self.assertEqual(
                [
                    {
                        "file_id": 41,
                        "docling_path": processed_doc.docling_document_path,
                        "total_pages": 2,
                        "neo4j_node_id": "doc-node-1",
                    }
                ],
                db.processing_results,
            )
            self.assertEqual(
                [
                    {
                        "file_id": 41,
                        "page_number": 1,
                        "content": "Page one",
                        "metadata": {"section": "intro"},
                        "neo4j_node_id": "page-node-1",
                    },
                    {
                        "file_id": 41,
                        "page_number": 2,
                        "content": "Page two",
                        "metadata": {"section": "body"},
                        "neo4j_node_id": "page-node-2",
                    },
                ],
                db.saved_pages,
            )
            self.assertEqual(
                [(document, str(markdown_path))],
                lightrag_handoff.calls,
            )
            self.assertEqual(
                {
                    "staged_input_path": "/app/data/inputs/000041.md",
                    "scan_requested": True,
                    "scan_response": {
                        "status": "scanning_started",
                        "track_id": "scan-41",
                    },
                },
                processed_doc.processing_metadata["lightrag"],
            )
            self.assertEqual(
                processed_doc.processing_metadata["lightrag"],
                metadata["lightrag"],
            )

    async def test_process_document_task_marks_error_when_processing_fails(self):
        with _patched_processing_dependencies():
            from app.routers.processing import process_document_task

            document = SimpleNamespace(id=77, filename="broken-file.pdf")
            db = FakeDatabase(document=document, resolved_path=Path("C:\\app\\data\\uploads\\broken-file.pdf"))
            docling = FakeDocling(
                processed_doc=None,
                pages=[],
                error=RuntimeError("docling exploded"),
            )
            neo4j = FakeNeo4j()

            with self.assertRaisesRegex(RuntimeError, "docling exploded"):
                await process_document_task(77, db, docling, neo4j)

            self.assertEqual(
                [(77, "processing", None), (77, "error", "docling exploded")],
                db.status_updates,
            )
            self.assertEqual([document], db.resolve_calls)
            self.assertEqual([(document, db.resolved_path)], docling.calls)
            self.assertEqual([], db.processing_results)
            self.assertEqual([], db.saved_pages)

    async def test_process_document_task_keeps_processing_successful_when_lightrag_handoff_fails(self):
        with _patched_processing_dependencies():
            from app.routers.processing import process_document_task

            document = SimpleNamespace(id=88, filename="handoff-file.pdf")
            processed_doc = SimpleNamespace(
                docling_document_path="/app/data/processed/documents/88/document.json",
                total_pages=1,
                neo4j_node_id=None,
                processing_metadata={"markdown_path": "/app/data/processed/documents/88/outputs/handoff-file.md"},
            )
            pages = [
                SimpleNamespace(page_number=1, content="Page one", metadata={"section": "intro"}),
            ]
            db = FakeDatabase(document=document, resolved_path=Path("C:\\app\\data\\uploads\\handoff-file.pdf"))
            docling = FakeDocling(processed_doc=processed_doc, pages=pages)
            neo4j = FakeNeo4j()
            lightrag_handoff = FakeLightRagHandoff(error=RuntimeError("scan endpoint unavailable"))

            await process_document_task(88, db, docling, neo4j, lightrag_handoff)

            self.assertEqual(
                [(88, "processing", None), (88, "processed", None)],
                db.status_updates,
            )
            self.assertEqual(
                {
                    "scan_requested": False,
                    "error": "scan endpoint unavailable",
                },
                processed_doc.processing_metadata["lightrag"],
            )


class LightRagHandoffServiceTests(unittest.TestCase):
    def test_handoff_stages_markdown_and_uses_documented_scan_endpoint(self):
        requests_seen = []

        class ScanHandler(BaseHTTPRequestHandler):
            def do_POST(self):  # noqa: N802 - stdlib callback name
                requests_seen.append(self.path)
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(
                    json.dumps(
                        {
                            "status": "scanning_started",
                            "message": "Scanning process has been initiated in the background",
                            "track_id": "scan-123",
                        }
                    ).encode("utf-8")
                )

            def log_message(self, format, *args):  # noqa: A003 - stdlib callback name
                return None

        with tempfile.TemporaryDirectory() as temp_dir:
            server = HTTPServer(("127.0.0.1", 0), ScanHandler)
            thread = Thread(target=server.serve_forever, daemon=True)
            thread.start()

            try:
                source_markdown = Path(temp_dir) / "source.md"
                source_markdown.write_text("# Report\n\nBody", encoding="utf-8")

                with _patched_database_dependencies():
                    from app.services.lightrag_handoff_service import LightRagHandoffService

                    service = LightRagHandoffService(
                        input_dir=Path(temp_dir) / "inputs",
                        service_url=f"http://127.0.0.1:{server.server_port}",
                    )

                    document = SimpleNamespace(
                        id=5,
                        filename="stored-file.pdf",
                        original_filename="Quarterly Report.pdf",
                    )

                    result = service.handoff_document(document, source_markdown)
            finally:
                server.shutdown()
                thread.join(timeout=5)
                server.server_close()

            staged_path = Path(result["staged_input_path"])
            self.assertTrue(staged_path.exists())
            self.assertEqual("# Report\n\nBody", staged_path.read_text(encoding="utf-8"))
            self.assertEqual(["/documents/scan"], requests_seen)
            self.assertEqual("scanning_started", result["scan_response"]["status"])
            self.assertEqual("scan-123", result["scan_response"]["track_id"])
            self.assertTrue(staged_path.name.startswith("000005-quarterly-report"))


class LightRagQueryServiceTests(unittest.TestCase):
    def test_query_data_uses_documented_query_data_endpoint(self):
        requests_seen = []

        class QueryHandler(BaseHTTPRequestHandler):
            def do_POST(self):  # noqa: N802 - stdlib callback name
                body = self.rfile.read(int(self.headers["Content-Length"])).decode("utf-8")
                requests_seen.append((self.path, json.loads(body)))
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(
                    json.dumps(
                        {
                            "status": "success",
                            "message": "retrieval complete",
                            "data": {"chunks": [{"content": "Revenue increased in Q1."}]},
                            "metadata": {"mode": "mix"},
                        }
                    ).encode("utf-8")
                )

            def log_message(self, format, *args):  # noqa: A003 - stdlib callback name
                return None

        server = HTTPServer(("127.0.0.1", 0), QueryHandler)
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()

        try:
            with _patched_database_dependencies():
                from app.models.models import LightRagQueryRequest
                from app.services.lightrag_query_service import LightRagQueryService

                service = LightRagQueryService(
                    service_url=f"http://127.0.0.1:{server.server_port}",
                    query_timeout_seconds=5,
                )

                result = service.query_data(
                    LightRagQueryRequest(
                        query="quarterly revenue",
                        mode="mix",
                        top_k=5,
                        chunk_top_k=3,
                    )
                )
        finally:
            server.shutdown()
            thread.join(timeout=5)
            server.server_close()

        self.assertEqual(
            [
                (
                    "/query/data",
                    {
                        "query": "quarterly revenue",
                        "mode": "mix",
                        "top_k": 5,
                        "chunk_top_k": 3,
                        "include_references": True,
                        "include_chunk_content": True,
                    },
                )
            ],
            requests_seen,
        )
        self.assertEqual("success", result["status"])
        self.assertEqual("retrieval complete", result["message"])
        self.assertEqual("mix", result["metadata"]["mode"])


class LightRagAppHostContractTests(unittest.TestCase):
    def test_apphost_uses_http_endpoint_and_explicit_neo4j_graph_storage(self):
        app_host_source = (REPO_ROOT / "src" / "AspireApp.AppHost" / "AppHost.cs").read_text(encoding="utf-8")

        self.assertIn('.WithEnvironment("LIGHTRAG_GRAPH_STORAGE", "Neo4JStorage")', app_host_source)
        self.assertIn('.WithHttpEndpoint(port: 9621, targetPort: 9621, name: "http")', app_host_source)
        self.assertIn('ReferenceExpression.Create(', app_host_source)
        self.assertIn('EndpointProperty.HostAndPort', app_host_source)
        self.assertIn('.WithEnvironment("NEO4J_URI", neo4jBoltUri)', app_host_source)
        self.assertIn('.WithEnvironment("NEO4J_BOLT_URL", neo4jBoltUri)', app_host_source)
        self.assertNotIn('.WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))', app_host_source)
        self.assertIn('pythonServices.WithEnvironment("LIGHTRAG_URL", lightrag.GetEndpoint("http"));', app_host_source)
        self.assertNotIn('pythonServices.WithEnvironment("LIGHTRAG_URL", lightrag.GetEndpoint("tcp"));', app_host_source)


if __name__ == "__main__":
    unittest.main()
