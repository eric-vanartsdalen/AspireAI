from __future__ import annotations

import inspect
import json
import sys
import unittest
from pathlib import Path

from pydantic import ValidationError


PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.contracts import (
    CanonicalDocument,
    Claim,
    Contradiction,
    EnvelopeMixin,
    Evidence,
    IKnowledgeRetriever,
    KnowledgeItem,
    KnowledgeResult,
    PageContent,
    ReasonResponse,
    ReasoningStep,
    ValidatedDocument,
)


class Phase1ContractTests(unittest.TestCase):
    def test_canonical_document_serializes_with_snake_case_envelope_fields(self):
        document = CanonicalDocument(
            tenant_id="tenant-a",
            correlation_id="corr-123",
            document_id=42,
            source_type="upload",
            source_confidence=0.95,
            pages=[
                PageContent(
                    page_number=1,
                    content="First page",
                    section="intro",
                    metadata={"language": "en"},
                )
            ],
            metadata={"category": "policy"},
        )

        payload = json.loads(document.model_dump_json())

        self.assertEqual("tenant-a", payload["tenant_id"])
        self.assertEqual("corr-123", payload["correlation_id"])
        self.assertEqual(42, payload["document_id"])
        self.assertEqual("upload", payload["source_type"])
        self.assertEqual(0.95, payload["source_confidence"])
        self.assertEqual("intro", payload["pages"][0]["section"])
        self.assertEqual({"language": "en"}, payload["pages"][0]["metadata"])
        self.assertNotIn("documentId", payload)

    def test_top_level_contracts_require_correlation_id(self):
        with self.assertRaises(ValidationError):
            CanonicalDocument(
                document_id=42,
                source_type="upload",
                source_confidence=0.95,
                pages=[],
                metadata={},
            )

    def test_validated_document_round_trips_with_nested_contracts(self):
        validated = ValidatedDocument(
            tenant_id="tenant-b",
            correlation_id="corr-456",
            document_id=1001,
            source_type="textbook",
            source_confidence=0.9,
            pages=[
                PageContent(
                    page_number=3,
                    content="Water boils at 100C.",
                    metadata={"chapter": "thermodynamics"},
                )
            ],
            metadata={"subject": "science"},
            claims=[
                Claim(
                    claim_id="claim-1",
                    text="Water boils at 100C at sea level.",
                    confidence=0.98,
                    source_ref="doc-1001#page-3",
                    evidence=[
                        Evidence(
                            content="Water boils at 100C.",
                            confidence=0.96,
                            source="doc-1001#page-3",
                        )
                    ],
                )
            ],
            contradictions=[
                Contradiction(
                    claim_id="claim-1",
                    conflicting_claim_id="claim-2",
                    description="Conflicts with high-altitude boiling point claim.",
                    confidence=0.73,
                )
            ],
            overall_confidence=0.91,
        )

        round_tripped = ValidatedDocument.model_validate_json(validated.model_dump_json())

        self.assertEqual(validated.model_dump(mode="json"), round_tripped.model_dump(mode="json"))

    def test_result_contracts_include_envelope_fields(self):
        knowledge_result = KnowledgeResult(
            tenant_id="tenant-c",
            correlation_id="corr-789",
            results=[
                KnowledgeItem(
                    content="Relevant retrieved fact",
                    confidence=0.82,
                    source_refs=["doc-1#page-2", "claim-7"],
                    relevance_score=0.88,
                )
            ],
        )
        reason_response = ReasonResponse(
            tenant_id="tenant-c",
            correlation_id="corr-789",
            answer="The evidence supports the retrieved fact.",
            confidence=0.84,
            evidence=[
                Evidence(
                    content="Relevant retrieved fact",
                    confidence=0.82,
                    source="doc-1#page-2",
                )
            ],
            reasoning_steps=[
                ReasoningStep(
                    step="retrieve",
                    reasoning="Search the knowledge layer first.",
                    tool="BrainKnowledgeRetriever",
                    result="Retrieved 1 matching item.",
                )
            ],
            proactive_suggestions=["Review the conflicting claim before acting."],
        )

        knowledge_payload = json.loads(knowledge_result.model_dump_json())
        reason_payload = json.loads(reason_response.model_dump_json())

        self.assertEqual("tenant-c", knowledge_payload["tenant_id"])
        self.assertEqual("corr-789", knowledge_payload["correlation_id"])
        self.assertEqual(["doc-1#page-2", "claim-7"], knowledge_payload["results"][0]["source_refs"])
        self.assertEqual("tenant-c", reason_payload["tenant_id"])
        self.assertEqual("corr-789", reason_payload["correlation_id"])
        self.assertEqual("retrieve", reason_payload["reasoning_steps"][0]["step"])

    def test_contract_exports_and_retriever_interface_are_available(self):
        self.assertTrue(issubclass(CanonicalDocument, EnvelopeMixin))
        self.assertTrue(inspect.isabstract(IKnowledgeRetriever))
        self.assertEqual(
            ("BrainKnowledgeRetriever", "LightRAGRetriever"),
            IKnowledgeRetriever.planned_implementations,
        )
        self.assertIn("retrieve", IKnowledgeRetriever.__abstractmethods__)


if __name__ == "__main__":
    unittest.main()
