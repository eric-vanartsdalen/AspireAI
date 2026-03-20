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

    def test_legacy_sync_methods_report_unified_schema(self):
        with _patched_dependencies():
            from app.services.database_service import DatabaseService

            with tempfile.TemporaryDirectory() as temp_dir:
                db_path = Path(temp_dir) / "data-resources.db"
                previous_db_path = os.environ.get("ASPIRE_DB_PATH")
                os.environ["ASPIRE_DB_PATH"] = str(db_path)
                try:
                    _close_database_pools(DatabaseService)
                    service = DatabaseService()
                    sync_status = service.get_file_document_sync_status()
                    sync_result = service.force_sync_files_and_documents()
                finally:
                    _close_database_pools(DatabaseService)
                    if previous_db_path is None:
                        os.environ.pop("ASPIRE_DB_PATH", None)
                    else:
                        os.environ["ASPIRE_DB_PATH"] = previous_db_path

                self.assertEqual("healthy", sync_status["sync_health"])
                self.assertEqual(sync_status["files_count"], sync_status["documents_count"])
                self.assertTrue(sync_result["sync_performed"])


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
