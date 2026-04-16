from __future__ import annotations

from typing import Any, Sequence
from uuid import uuid4

from ...contracts import CanonicalDocument, PageContent as ContractPageContent
from ...models.models import Document, PageContent as ExtractedPageContent

DEFAULT_SOURCE_CONFIDENCE_BY_TYPE = {
    "api": 0.5,
    "file": 0.7,
    "general_file": 0.7,
    "note": 0.3,
    "textbook": 0.9,
    "textbook_pdf": 0.9,
    "upload": 0.7,
    "url": 0.5,
    "youtube_video": 0.4,
    "youtube_channel": 0.5,
    "user_note": 0.3,
}


def normalize_source_type(source_type: str | None) -> str:
    normalized = (source_type or "upload").strip().lower().replace("-", "_")
    return normalized or "upload"


def resolve_source_confidence(
    *,
    source_type: str | None,
    mime_type: str | None = None,
    file_name: str | None = None,
    explicit_confidence: float | None = None,
) -> float:
    if explicit_confidence is not None:
        return max(0.0, min(1.0, float(explicit_confidence)))

    normalized_source_type = normalize_source_type(source_type)
    normalized_file_name = (file_name or "").lower()

    if normalized_source_type in {"textbook", "textbook_pdf"}:
        return DEFAULT_SOURCE_CONFIDENCE_BY_TYPE["textbook"]

    if (
        normalized_source_type in {"upload", "file", "general_file"}
        and (mime_type or "").lower() == "application/pdf"
        and "textbook" in normalized_file_name
    ):
        return DEFAULT_SOURCE_CONFIDENCE_BY_TYPE["textbook"]

    return DEFAULT_SOURCE_CONFIDENCE_BY_TYPE.get(normalized_source_type, 0.7)


def build_ingestion_correlation_id(document_id: int) -> str:
    return f"ingest-{document_id}-{uuid4().hex[:12]}"


def build_canonical_document(
    document: Document,
    pages: Sequence[ExtractedPageContent | ContractPageContent],
    *,
    correlation_id: str | None = None,
    metadata: dict[str, Any] | None = None,
) -> CanonicalDocument:
    source_type = normalize_source_type(getattr(document, "source_type", "upload"))
    filename = getattr(document, "filename", f"document-{document.id}")
    original_filename = getattr(document, "original_filename", filename)
    upload_date = getattr(document, "upload_date", None)
    canonical_pages = [
        ContractPageContent(
            page_number=page.page_number,
            content=page.content,
            section=_resolve_page_section(page),
            metadata=dict(getattr(page, "metadata", None) or {}),
        )
        for page in pages
    ]

    canonical_metadata = {
        "file_name": filename,
        "original_file_name": original_filename,
        "file_path": getattr(document, "file_path", ""),
        "file_size": getattr(document, "file_size", None),
        "mime_type": getattr(document, "mime_type", None),
        "upload_date": upload_date.isoformat() if upload_date is not None else None,
    }

    source_url = getattr(document, "source_url", None)
    if source_url:
        canonical_metadata["source_url"] = source_url

    if metadata:
        canonical_metadata.update(metadata)

    return CanonicalDocument(
        tenant_id=getattr(document, "tenant_id", "default") or "default",
        correlation_id=correlation_id or build_ingestion_correlation_id(document.id),
        document_id=document.id,
        source_type=source_type,
        source_confidence=resolve_source_confidence(
            source_type=source_type,
            mime_type=getattr(document, "mime_type", None),
            file_name=original_filename or filename,
            explicit_confidence=getattr(document, "source_confidence", None),
        ),
        pages=canonical_pages,
        metadata=canonical_metadata,
    )


def _resolve_page_section(page: ExtractedPageContent | ContractPageContent) -> str | None:
    section = getattr(page, "section", None)
    if section:
        return section

    metadata = getattr(page, "metadata", None) or {}
    if not isinstance(metadata, dict):
        return None

    metadata_section = metadata.get("section")
    return metadata_section if isinstance(metadata_section, str) else None
