"""BRAIN ingestion package scaffolding."""
from .canonicalization import (
    DEFAULT_SOURCE_CONFIDENCE_BY_TYPE,
    build_canonical_document,
    build_ingestion_correlation_id,
    normalize_source_type,
    resolve_source_confidence,
)

__all__ = [
    "DEFAULT_SOURCE_CONFIDENCE_BY_TYPE",
    "build_canonical_document",
    "build_ingestion_correlation_id",
    "normalize_source_type",
    "resolve_source_confidence",
]
