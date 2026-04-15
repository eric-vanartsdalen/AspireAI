"""Shared Python-side BRAIN contract models and interfaces."""

from .interfaces import IKnowledgeRetriever
from .models import (
    BrainChatRequest,
    BrainQueryRequest,
    BrainContractModel,
    CanonicalDocument,
    ChatMode,
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
    "BrainChatRequest",
    "BrainContractModel",
    "BrainQueryRequest",
    "CanonicalDocument",
    "ChatMode",
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
