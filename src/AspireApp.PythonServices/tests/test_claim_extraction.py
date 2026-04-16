"""Tests for claim extraction service."""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.services.claim_extraction_service import ClaimExtractionService


class ClaimExtractionServiceTests(unittest.TestCase):
    def test_extract_claims_splits_sentences(self):
        service = ClaimExtractionService()
        content = "Aspire coordinates services. It simplifies orchestration. Docker containers are managed automatically."
        
        claims = service.extract_claims(content, source_confidence=0.7)
        
        self.assertEqual(3, len(claims))
        self.assertTrue(claims[0]["text"].startswith("Aspire coordinates"))
        self.assertTrue(claims[1]["text"].startswith("It simplifies"))
        self.assertTrue(claims[2]["text"].startswith("Docker containers"))
    
    def test_extract_claims_assigns_confidence_based_on_source(self):
        service = ClaimExtractionService()
        content = "Textbooks provide reliable information. Academic papers are peer-reviewed."
        
        claims = service.extract_claims(content, source_confidence=0.9)
        
        # Should use source confidence as base (may be adjusted by heuristics)
        self.assertGreater(claims[0]["confidence"], 0.5)
        self.assertLessEqual(claims[0]["confidence"], 1.0)
    
    def test_extract_claims_filters_short_fragments(self):
        service = ClaimExtractionService(min_claim_length=20)
        content = "Yes. Aspire is a framework. It orchestrates distributed services."
        
        claims = service.extract_claims(content, source_confidence=0.7)
        
        # "Yes." should be filtered out as too short
        self.assertEqual(2, len(claims))
        self.assertEqual("Aspire is a framework", claims[0]["text"])
    
    def test_extract_claims_returns_empty_for_blank_content(self):
        service = ClaimExtractionService()
        
        claims = service.extract_claims("", source_confidence=0.7)
        
        self.assertEqual(0, len(claims))
    
    def test_extract_claims_includes_metadata(self):
        service = ClaimExtractionService()
        content = "Neo4j stores graph data efficiently."
        
        claims = service.extract_claims(content, source_confidence=0.85, source_type="manual")
        
        self.assertEqual(1, len(claims))
        self.assertEqual("manual", claims[0]["source_type"])
        self.assertEqual("sentence_split", claims[0]["metadata"]["extraction_method"])
        self.assertEqual(0.85, claims[0]["metadata"]["source_confidence"])


if __name__ == "__main__":
    unittest.main()
