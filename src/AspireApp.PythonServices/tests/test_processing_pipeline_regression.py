import gc
import os
import shutil
import sys
import types
import unittest
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace
from uuid import uuid4


PROJECT_ROOT = Path(__file__).resolve().parents[1]
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

    module.APIRouter = APIRouter
    module.HTTPException = HTTPException
    module.Depends = Depends
    module.BackgroundTasks = BackgroundTasks
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


class ProcessDocumentTaskRegressionTests(unittest.IsolatedAsyncioTestCase):
    async def test_process_document_task_marks_processed_and_persists_pages(self):
        with _patched_processing_dependencies():
            from app.routers.processing import process_document_task

            document = SimpleNamespace(id=41, filename="stored-file.pdf")
            processed_doc = SimpleNamespace(
                docling_document_path="/app/data/processed/documents/41/document.json",
                total_pages=2,
                neo4j_node_id=None,
            )
            pages = [
                SimpleNamespace(page_number=1, content="Page one", metadata={"section": "intro"}),
                SimpleNamespace(page_number=2, content="Page two", metadata={"section": "body"}),
            ]
            db = FakeDatabase(document=document, resolved_path=Path("C:\\app\\data\\uploads\\stored-file.pdf"))
            docling = FakeDocling(processed_doc=processed_doc, pages=pages)
            neo4j = FakeNeo4j()

            await process_document_task(41, db, docling, neo4j)

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


if __name__ == "__main__":
    unittest.main()
