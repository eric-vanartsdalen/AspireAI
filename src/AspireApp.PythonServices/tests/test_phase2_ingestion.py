from __future__ import annotations

import sys
import unittest
from datetime import UTC, datetime
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.brain.ingestion import build_canonical_document, resolve_source_confidence
from app.models.models import Document, PageContent


class Phase2IngestionTests(unittest.TestCase):
    def test_source_confidence_defaults_follow_phase_two_roadmap(self):
        self.assertEqual(0.9, resolve_source_confidence(source_type="textbook"))
        self.assertEqual(0.7, resolve_source_confidence(source_type="upload"))
        self.assertEqual(0.5, resolve_source_confidence(source_type="url"))
        self.assertEqual(0.3, resolve_source_confidence(source_type="user_note"))

    def test_canonical_document_builds_from_document_and_pages(self):
        document = Document(
            id=42,
            filename="stored-file.pdf",
            original_filename="science-textbook.pdf",
            file_path="C:\\data\\uploads",
            file_size=1024,
            mime_type="application/pdf",
            upload_date=datetime(2026, 7, 15, 12, 0, tzinfo=UTC),
            processing_status="processing",
            tenant_id="tenant-a",
            source_type="upload",
        )

        canonical = build_canonical_document(
            document,
            [
                PageContent(page_number=1, content="Page 1", metadata={"section": "intro"}),
                PageContent(page_number=2, content="Page 2", metadata={"page": 2}),
            ],
            correlation_id="corr-42",
        )

        self.assertEqual("tenant-a", canonical.tenant_id)
        self.assertEqual("corr-42", canonical.correlation_id)
        self.assertEqual(42, canonical.document_id)
        self.assertEqual("upload", canonical.source_type)
        self.assertEqual(0.9, canonical.source_confidence)
        self.assertEqual("intro", canonical.pages[0].section)
        self.assertEqual("science-textbook.pdf", canonical.metadata["original_file_name"])
        self.assertEqual("2026-07-15T12:00:00+00:00", canonical.metadata["upload_date"])


if __name__ == "__main__":
    unittest.main()
