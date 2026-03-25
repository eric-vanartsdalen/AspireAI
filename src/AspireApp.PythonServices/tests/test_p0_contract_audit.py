import gc
import os
import shutil
import sqlite3
import sys
import tempfile
import types
import unittest
from contextlib import contextmanager
from datetime import UTC, datetime
from pathlib import Path
from types import SimpleNamespace


PROJECT_ROOT = Path(__file__).resolve().parents[1]


def _purge_app_modules() -> None:
    """Clear cached AspireApp Python service modules so test doubles take effect."""
    for module_name in list(sys.modules):
        if module_name == "app" or module_name.startswith("app."):
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


def _install_fake_docling() -> type:
    docling_module = types.ModuleType("docling")
    document_converter_module = types.ModuleType("docling.document_converter")
    datamodel_module = types.ModuleType("docling.datamodel")
    base_models_module = types.ModuleType("docling.datamodel.base_models")
    pipeline_options_module = types.ModuleType("docling.datamodel.pipeline_options")
    backend_module = types.ModuleType("docling.backend")
    backend_pdfium_module = types.ModuleType("docling.backend.pypdfium2_backend")

    class FakeDoc:
        def iterate_items(self):
            return []

        def export_to_dict(self):
            return {}

    class FakeDocumentConverter:
        calls = []

        def convert(self, file_path):
            self.__class__.calls.append(file_path)
            return SimpleNamespace(document=FakeDoc())

    document_converter_module.DocumentConverter = FakeDocumentConverter
    base_models_module.InputFormat = object
    pipeline_options_module.PdfPipelineOptions = object
    backend_pdfium_module.PyPdfiumDocumentBackend = object

    sys.modules["docling"] = docling_module
    sys.modules["docling.document_converter"] = document_converter_module
    sys.modules["docling.datamodel"] = datamodel_module
    sys.modules["docling.datamodel.base_models"] = base_models_module
    sys.modules["docling.datamodel.pipeline_options"] = pipeline_options_module
    sys.modules["docling.backend"] = backend_module
    sys.modules["docling.backend.pypdfium2_backend"] = backend_pdfium_module

    return FakeDocumentConverter


@contextmanager
def _patched_dependencies(include_docling: bool = False):
    original_modules = {
        name: sys.modules.get(name)
        for name in [
            "pydantic",
            "docling",
            "docling.document_converter",
            "docling.datamodel",
            "docling.datamodel.base_models",
            "docling.datamodel.pipeline_options",
            "docling.backend",
            "docling.backend.pypdfium2_backend",
        ]
    }

    _purge_app_modules()
    _install_fake_pydantic()
    fake_converter = _install_fake_docling() if include_docling else None
    sys.path.insert(0, str(PROJECT_ROOT))

    try:
        yield fake_converter
    finally:
        sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
        _purge_app_modules()

        for name, module in original_modules.items():
            if module is None:
                sys.modules.pop(name, None)
            else:
                sys.modules[name] = module


class DatabaseContractAuditTests(unittest.TestCase):
    def test_database_schema_is_minimized_to_files_and_document_pages(self):
        with _patched_dependencies():
            from app.services.database_service import DatabaseService

            temp_dir = Path(tempfile.mkdtemp())
            db_path = temp_dir / "data-resources.db"
            previous_db_path = os.environ.get("ASPIRE_DB_PATH")
            os.environ["ASPIRE_DB_PATH"] = str(db_path)
            try:
                _close_database_pools(DatabaseService)
                DatabaseService()

                with sqlite3.connect(db_path) as conn:
                    rows = conn.execute(
                        "SELECT name FROM sqlite_master WHERE type = 'table'"
                    ).fetchall()

                table_names = {row[0] for row in rows}
                self.assertTrue({"files", "document_pages"}.issubset(table_names))
                self.assertFalse({"documents", "processed_documents"} & table_names)
            finally:
                _close_database_pools(DatabaseService)
                gc.collect()
                if previous_db_path is None:
                    os.environ.pop("ASPIRE_DB_PATH", None)
                else:
                    os.environ["ASPIRE_DB_PATH"] = previous_db_path
                shutil.rmtree(temp_dir, ignore_errors=True)

    def test_database_service_exposes_canonical_files_surface(self):
        with _patched_dependencies():
            from app.services.database_service import DatabaseService

            with tempfile.TemporaryDirectory() as temp_dir:
                db_path = Path(temp_dir) / "data-resources.db"
                previous_db_path = os.environ.get("ASPIRE_DB_PATH")
                os.environ["ASPIRE_DB_PATH"] = str(db_path)
                try:
                    _close_database_pools(DatabaseService)
                    service = DatabaseService()
                    file_id = service.create_file_record(
                        file_name="stored-file.pdf",
                        original_file_name="original.pdf",
                        file_path=str(Path(temp_dir) / "uploads"),
                        file_size=4,
                        mime_type="application/pdf",
                        status="uploaded",
                    )

                    document = service.get_document_by_id(file_id)
                    unprocessed = service.list_unprocessed_documents()

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
                    status = service.get_processing_status(file_id)
                    pages = service.get_document_pages(file_id)
                finally:
                    _close_database_pools(DatabaseService)
                    if previous_db_path is None:
                        os.environ.pop("ASPIRE_DB_PATH", None)
                    else:
                        os.environ["ASPIRE_DB_PATH"] = previous_db_path

                self.assertFalse(hasattr(service, "get_file_document_sync_status"))
                self.assertFalse(hasattr(service, "force_sync_files_and_documents"))
                self.assertFalse(hasattr(service, "save_document"))
                self.assertFalse(hasattr(service, "update_processing_status"))
                self.assertFalse(hasattr(service, "get_unprocessed_documents"))
                self.assertFalse(hasattr(service, "get_processed_document"))

                self.assertEqual(file_id, document.id)
                self.assertEqual("uploaded", document.processing_status)
                self.assertEqual(1, len(unprocessed))
                self.assertEqual("processed", status.status)
                self.assertEqual(3, status.total_pages)
                self.assertEqual(1, len(pages))
                self.assertEqual("Persisted page content", pages[0]["content"])

    def test_unprocessed_documents_include_retryable_error_rows(self):
        with _patched_dependencies():
            from app.services.database_service import DatabaseService

            with tempfile.TemporaryDirectory() as temp_dir:
                db_path = Path(temp_dir) / "data-resources.db"
                previous_db_path = os.environ.get("ASPIRE_DB_PATH")
                os.environ["ASPIRE_DB_PATH"] = str(db_path)
                try:
                    _close_database_pools(DatabaseService)
                    service = DatabaseService()

                    uploaded_id = service.create_file_record(
                        file_name="uploaded.pdf",
                        original_file_name="uploaded.pdf",
                        file_path=str(Path(temp_dir) / "uploads"),
                        status="uploaded",
                    )
                    failed_id = service.create_file_record(
                        file_name="failed.pdf",
                        original_file_name="failed.pdf",
                        file_path=str(Path(temp_dir) / "uploads"),
                        status="uploaded",
                    )
                    processing_id = service.create_file_record(
                        file_name="processing.pdf",
                        original_file_name="processing.pdf",
                        file_path=str(Path(temp_dir) / "uploads"),
                        status="uploaded",
                    )
                    processed_id = service.create_file_record(
                        file_name="processed.pdf",
                        original_file_name="processed.pdf",
                        file_path=str(Path(temp_dir) / "uploads"),
                        status="uploaded",
                    )

                    service.update_file_status(failed_id, "processing")
                    service.update_file_status(failed_id, "error", "boom")
                    service.update_file_status(processing_id, "processing")
                    service.update_file_status(processed_id, "processing")
                    service.update_file_status(processed_id, "processed")

                    retryable_ids = {
                        doc.id for doc in service.list_unprocessed_documents()
                    }
                finally:
                    _close_database_pools(DatabaseService)
                    if previous_db_path is None:
                        os.environ.pop("ASPIRE_DB_PATH", None)
                    else:
                        os.environ["ASPIRE_DB_PATH"] = previous_db_path

                self.assertEqual({uploaded_id, failed_id}, retryable_ids)

    def test_processing_status_resets_stale_artifacts_on_retry(self):
        with _patched_dependencies():
            from app.services.database_service import DatabaseService

            with tempfile.TemporaryDirectory() as temp_dir:
                db_path = Path(temp_dir) / "data-resources.db"
                previous_db_path = os.environ.get("ASPIRE_DB_PATH")
                os.environ["ASPIRE_DB_PATH"] = str(db_path)
                try:
                    _close_database_pools(DatabaseService)
                    service = DatabaseService()
                    file_id = service.create_file_record(
                        file_name="retry.pdf",
                        original_file_name="retry.pdf",
                        file_path=str(Path(temp_dir) / "uploads"),
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

                    service.update_file_status(file_id, "processing")

                    record = service.get_file_by_id(file_id)
                    status = service.get_processing_status(file_id)
                    pages = service.get_document_pages(file_id)
                finally:
                    _close_database_pools(DatabaseService)
                    if previous_db_path is None:
                        os.environ.pop("ASPIRE_DB_PATH", None)
                    else:
                        os.environ["ASPIRE_DB_PATH"] = previous_db_path

                self.assertEqual("processing", record["status"])
                self.assertIsNotNone(record["processing_started_at"])
                self.assertIsNone(record["processing_completed_at"])
                self.assertIsNone(record["processing_error"])
                self.assertIsNone(record["docling_document_path"])
                self.assertIsNone(record["total_pages"])
                self.assertIsNone(record["neo4j_document_node_id"])
                self.assertEqual([], pages)
                self.assertEqual("processing", status.status)
                self.assertIsNone(status.error_message)
                self.assertIsNone(status.completed_at)


def _close_database_pools(database_service_type) -> None:
    for pool in database_service_type._pools.values():
        pool.close_all()
    database_service_type._pools.clear()


class UploadPathNormalizationAuditTests(unittest.TestCase):
    """Regression gate: verify the live resolve_upload_path → DoclingService contract."""

    def _run_with_resolved_path(self, fake_converter, document, temp_dir, uploaded_file):
        """Shared helper: resolve path via DatabaseService, then drive DoclingService."""
        from app.services.database_service import DatabaseService
        from app.services.docling_service import DoclingService

        db_path = Path(temp_dir) / "data-resources.db"
        previous_db = os.environ.get("ASPIRE_DB_PATH")
        previous_data = os.environ.get("ASPIRE_DATA_PATH")
        os.environ["ASPIRE_DB_PATH"] = str(db_path)
        os.environ["ASPIRE_DATA_PATH"] = temp_dir
        try:
            _close_database_pools(DatabaseService)
            db = DatabaseService()

            resolved_path = db.resolve_upload_path(document)

            fake_converter.calls.clear()
            docling = DoclingService(data_path=temp_dir)
            docling.process_document(document, resolved_path)

            self.assertEqual(
                str(uploaded_file.resolve()), fake_converter.calls[-1]
            )
        finally:
            _close_database_pools(DatabaseService)
            gc.collect()
            if previous_db is None:
                os.environ.pop("ASPIRE_DB_PATH", None)
            else:
                os.environ["ASPIRE_DB_PATH"] = previous_db
            if previous_data is None:
                os.environ.pop("ASPIRE_DATA_PATH", None)
            else:
                os.environ["ASPIRE_DATA_PATH"] = previous_data

    def test_docling_should_join_container_directory_and_stored_filename(self):
        with _patched_dependencies(include_docling=True) as fake_converter:
            from app.models.models import Document

            with tempfile.TemporaryDirectory() as temp_dir:
                uploads_dir = Path(temp_dir) / "uploads"
                uploads_dir.mkdir(parents=True, exist_ok=True)
                uploaded_file = uploads_dir / "stored-file.pdf"
                uploaded_file.write_text("stub", encoding="utf-8")

                document = Document(
                    id=1,
                    filename="stored-file.pdf",
                    original_filename="original.pdf",
                    file_path=str(uploads_dir),
                    file_size=4,
                    mime_type="application/pdf",
                    upload_date=datetime.now(UTC),
                    processed=False,
                    processing_status="pending",
                )

                self._run_with_resolved_path(
                    fake_converter, document, temp_dir, uploaded_file
                )

    def test_docling_should_guard_windows_directory_values_from_the_database(self):
        with _patched_dependencies(include_docling=True) as fake_converter:
            from app.models.models import Document

            with tempfile.TemporaryDirectory() as temp_dir:
                uploads_dir = Path(temp_dir) / "uploads"
                uploads_dir.mkdir(parents=True, exist_ok=True)
                uploaded_file = uploads_dir / "stored-file.pdf"
                uploaded_file.write_text("stub", encoding="utf-8")

                document = Document(
                    id=2,
                    filename="stored-file.pdf",
                    original_filename="original.pdf",
                    file_path=r"C:\Users\ericv\source\repos\AspireAI\data\uploads",
                    file_size=4,
                    mime_type="application/pdf",
                    upload_date=datetime.now(UTC),
                    processed=False,
                    processing_status="pending",
                )

                self._run_with_resolved_path(
                    fake_converter, document, temp_dir, uploaded_file
                )


if __name__ == "__main__":
    unittest.main()
