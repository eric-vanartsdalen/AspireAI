"""Shared Python-side BRAIN contract models and interfaces."""

from .interfaces import IKnowledgeRetriever
from .models import (
    BrainContractModel,
    CanonicalDocument,
    Claim,
    Contradiction,
    EnvelopeMixin,
    Evidence,
    KnowledgeItem,
    KnowledgeResult,
    PageContent,
    ReasonResponse,
    ReasoningStep,
    ValidatedDocument,
)

__all__ = [
    "BrainContractModel",
    "CanonicalDocument",
    "Claim",
    "Contradiction",
    "EnvelopeMixin",
    "Evidence",
    "IKnowledgeRetriever",
    "KnowledgeItem",
    "KnowledgeResult",
    "PageContent",
    "ReasonResponse",
    "ReasoningStep",
    "ValidatedDocument",
]
