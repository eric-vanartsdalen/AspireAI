from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Any, ClassVar

from .models import KnowledgeResult


class IKnowledgeRetriever(ABC):
    """Retrieval abstraction for BrainKnowledgeRetriever and LightRAGRetriever."""

    planned_implementations: ClassVar[tuple[str, str]] = (
        "BrainKnowledgeRetriever",
        "LightRAGRetriever",
    )

    @abstractmethod
    async def retrieve(
        self,
        query: str,
        *,
        tenant_id: str = "default",
        correlation_id: str | None = None,
        limit: int = 10,
        **options: Any,
    ) -> KnowledgeResult:
        """Return confidence-scored knowledge results for a tenant-scoped query."""
