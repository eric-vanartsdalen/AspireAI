"""
Dependency-tolerant smoke checks for Python services.
"""
import sys
import unittest
from importlib import import_module
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parent
REPO_ROOT = PROJECT_ROOT.parents[1]
DATA_ROOT = REPO_ROOT / "data"


def _ensure_project_root_on_path() -> None:
    project_root = str(PROJECT_ROOT)
    sys.path = [path for path in sys.path if path != project_root]
    sys.path.insert(0, project_root)


_ensure_project_root_on_path()


def _load_service(module_name: str, class_name: str, optional_package: str | None = None):
    _ensure_project_root_on_path()
    try:
        module = import_module(module_name)
    except ModuleNotFoundError as exc:
        if optional_package and (
            exc.name == optional_package or exc.name.startswith(f"{optional_package}.")
        ):
            return None, exc
        raise

    return getattr(module, class_name), None


class ServiceSmokeTests(unittest.TestCase):
    def test_database_service_initializes_with_current_api(self):
        database_service, import_error = _load_service(
            "app.services.database_service",
            "DatabaseService",
        )
        self.assertIsNone(import_error)

        db = database_service()
        documents = db.list_documents()

        self.assertIsInstance(documents, list)

    def test_document_processing_service_factory_initializes_with_available_dependencies(self):
        _ensure_project_root_on_path()
        service_factory = import_module("app.services.service_factory")

        service = service_factory.DoclingService(data_path=str(DATA_ROOT))
        service_info = service_factory.get_service_info()

        self.assertIsNotNone(service)
        self.assertIn(service_info["service_type"], {"full", "fallback"})
        self.assertEqual(
            service_info["docling_available"],
            service_info["service_type"] == "full",
        )

        expected_module = (
            "app.services.docling_service"
            if service_info["docling_available"]
            else "app.services.docling_service_fallback"
        )
        self.assertEqual(service.__class__.__module__, expected_module)

    def test_neo4j_service_health_check_is_tolerant_when_driver_is_available(self):
        neo4j_service, import_error = _load_service(
            "app.services.neo4j_service",
            "Neo4jService",
            optional_package="neo4j",
        )
        if import_error is not None:
            self.skipTest(f"Optional dependency 'neo4j' is not installed: {import_error}")

        service = neo4j_service()
        try:
            self.assertIsInstance(service.health_check(), bool)
        finally:
            service.close()


if __name__ == "__main__":
    unittest.main()
