from __future__ import annotations

from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class BrainContractModel(BaseModel):
    """Base class for strict Phase 1 BRAIN contract models."""

    model_config = ConfigDict(extra="forbid")


class EnvelopeMixin(BrainContractModel):
    """Tenant and tracing envelope shared by top-level BRAIN contracts."""

    tenant_id: str = "default"
    correlation_id: str


class PageContent(BrainContractModel):
    """Normalized page content emitted by the ingestion layer."""

    page_number: int
    content: str
    section: str | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)


class Evidence(BrainContractModel):
    """Supporting source passage attached to a claim or response."""

    content: str
    confidence: float
    source: str


class Claim(BrainContractModel):
    """Validated statement extracted from a canonical document."""

    claim_id: str
    text: str
    confidence: float
    evidence: list[Evidence] = Field(default_factory=list)
    source_ref: str


class Contradiction(BrainContractModel):
    """Conflict between two claims discovered during validation."""

    claim_id: str
    conflicting_claim_id: str
    description: str
    confidence: float


class CanonicalDocument(EnvelopeMixin):
    """Canonical document shape shared between ingestion and downstream layers."""

    document_id: int
    source_type: str
    source_confidence: float
    pages: list[PageContent] = Field(default_factory=list)
    metadata: dict[str, Any] = Field(default_factory=dict)


class ValidatedDocument(CanonicalDocument):
    """Canonical document enriched with extracted claims and contradictions."""

    claims: list[Claim] = Field(default_factory=list)
    contradictions: list[Contradiction] = Field(default_factory=list)
    overall_confidence: float


class KnowledgeItem(BrainContractModel):
    """Single confidence-scored retrieval hit returned from the knowledge layer."""

    content: str
    confidence: float
    source_refs: list[str] = Field(default_factory=list)
    relevance_score: float


class KnowledgeResult(EnvelopeMixin):
    """Envelope for tenant-scoped knowledge retrieval results."""

    results: list[KnowledgeItem] = Field(default_factory=list)


class ReasoningStep(BrainContractModel):
    """Traceable reasoning step produced while forming a response."""

    step: str
    reasoning: str
    tool: str | None = None
    result: str


class ReasonResponse(EnvelopeMixin):
    """Evidence-backed response emitted by the reasoning layer."""

    answer: str
    confidence: float
    evidence: list[Evidence] = Field(default_factory=list)
    reasoning_steps: list[ReasoningStep] = Field(default_factory=list)
    proactive_suggestions: list[str] = Field(default_factory=list)
