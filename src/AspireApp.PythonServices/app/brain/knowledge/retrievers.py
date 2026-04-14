from __future__ import annotations

import asyncio
import json
import uuid
from collections.abc import Mapping
from typing import Any, Iterable

from ...contracts import IKnowledgeRetriever, KnowledgeItem, KnowledgeResult
from ...models.models import LightRagQueryRequest
from ...services.lightrag_query_service import LightRagQueryService
from ...services.neo4j_service import Neo4jService

DEFAULT_CONFIDENCE = 0.5
SCORE_KEYS = ("confidence", "relevance_score", "score", "similarity", "source_confidence")


class _KnowledgeItemFactory:
    def __init__(self):
        # Allow subclasses to provide Neo4j service for confidence enrichment
        self._neo4j_service: Neo4jService | None = getattr(self, '_neo4j_service', None)

    def _build_item(self, item: Any, *, enrich_confidence: bool = False) -> KnowledgeItem | None:
        content = ""
        if isinstance(item, Mapping):
            content = self._first_string(
                item,
                ("content", "text", "chunk_content", "chunk", "response", "answer", "result"),
            ) or ""
        elif isinstance(item, str):
            content = item
        else:
            content = str(item)

        if not content and isinstance(item, Mapping):
            content = json.dumps(item, ensure_ascii=False, default=str)

        confidence = self._first_float(item, SCORE_KEYS)
        if confidence is None:
            confidence = self._first_float(self._metadata(item), SCORE_KEYS)
        
        # NEW: Enrich confidence from Neo4j when LightRAG doesn't provide a score
        if confidence is None and enrich_confidence and self._neo4j_service is not None:
            confidence = self._enrich_confidence_from_provenance(item)
        
        # If still no confidence after enrichment, fail closed: return None
        # This signals unresolved confidence and allows fallback to semantic retriever
        # Do NOT use DEFAULT_CONFIDENCE for unresolved scores
        if confidence is None:
            return None
        
        source_refs = self._extract_source_refs(item)

        return KnowledgeItem(
            content=content,
            confidence=confidence,
            source_refs=source_refs,
            relevance_score=confidence,
        )
    
    def _enrich_confidence_from_provenance(self, item: Any) -> float | None:
        """
        Attempt to enrich confidence from stored Neo4j data when LightRAG omits score.
        Returns None if provenance cannot be resolved or Neo4j lookup fails.
        """
        if not isinstance(item, Mapping):
            return None
        
        # Extract document_id and page_number from the item
        document_id = self._extract_int(item, "document_id")
        page_number = self._extract_int(item, "page_number")
        
        if document_id is None:
            # Try to parse from source_refs if present
            source_refs = self._extract_source_refs(item)
            for ref in source_refs:
                parsed = self._parse_provenance_from_ref(ref)
                if parsed and parsed.get("document_id") is not None:
                    document_id = parsed["document_id"]
                    page_number = parsed.get("page_number")
                    break
        
        if document_id is None:
            return None
        
        try:
            return self._neo4j_service.get_confidence_by_provenance(document_id, page_number)
        except Exception:
            # Neo4j query failed; return None to signal unresolved confidence
            return None
    
    def _parse_provenance_from_ref(self, ref: str) -> dict[str, int | None]:
        """
        Parse document_id and page_number from reference strings like:
        - "document:7/page:2"
        - "document:7"
        """
        result: dict[str, int | None] = {"document_id": None, "page_number": None}
        
        if not isinstance(ref, str):
            return result
        
        # Try to extract document:N/page:M pattern
        if "document:" in ref:
            parts = ref.split("/")
            doc_part = next((p for p in parts if "document:" in p), None)
            if doc_part:
                try:
                    result["document_id"] = int(doc_part.split(":")[1])
                except (ValueError, IndexError):
                    pass
            
            page_part = next((p for p in parts if "page:" in p), None)
            if page_part:
                try:
                    result["page_number"] = int(page_part.split(":")[1])
                except (ValueError, IndexError):
                    pass
        
        return result

    @staticmethod
    def _first_list(payload: Mapping[str, Any], keys: Iterable[str]) -> list[Any] | None:
        for key in keys:
            if isinstance(payload.get(key), list):
                return payload[key]
        return None

    @staticmethod
    def _first_string(payload: Mapping[str, Any], keys: Iterable[str]) -> str | None:
        for key in keys:
            value = payload.get(key)
            if isinstance(value, str) and value.strip():
                return value
        return None

    @staticmethod
    def _first_float(payload: Any, keys: Iterable[str]) -> float | None:
        if not isinstance(payload, Mapping):
            return None
        for key in keys:
            value = payload.get(key)
            if isinstance(value, bool):
                continue
            if isinstance(value, (int, float)):
                return float(value)
            if isinstance(value, str):
                try:
                    return float(value)
                except ValueError:
                    continue
        return None

    @staticmethod
    def _metadata(item: Any) -> Mapping[str, Any]:
        if isinstance(item, Mapping) and isinstance(item.get("metadata"), Mapping):
            return item["metadata"]
        return {}

    def _extract_source_refs(self, item: Any) -> list[str]:
        if not isinstance(item, Mapping):
            return []

        refs = self._extract_refs(
            item.get("source_ref")
            or item.get("source")
            or item.get("source_refs")
            or item.get("references")
            or item.get("sources")
        )
        if not refs:
            metadata = self._metadata(item)
            refs = self._extract_refs(
                metadata.get("source_ref")
                or metadata.get("source")
                or metadata.get("source_refs")
                or metadata.get("references")
                or metadata.get("sources")
            )

        if refs:
            return self._dedupe_preserving_order(refs)

        generated: list[str] = []
        document_id = self._extract_int(item, "document_id")
        page_number = self._extract_int(item, "page_number")
        if document_id is not None:
            if page_number is not None:
                generated.append(f"document:{document_id}/page:{page_number}")
            else:
                generated.append(f"document:{document_id}")

        filename = self._first_string(item, ("filename", "file_name", "source"))
        if filename:
            generated.append(f"file:{filename}")

        return generated

    def _extract_refs(self, value: Any) -> list[str]:
        if isinstance(value, str) and value.strip():
            return [value]
        if isinstance(value, Mapping):
            explicit_ref = self._first_string(value, ("source_ref", "ref", "reference"))
            if explicit_ref:
                return [explicit_ref]

            document_id = self._extract_int(value, "document_id") or self._extract_int(value, "id")
            page_number = self._extract_int(value, "page_number")
            filename = self._first_string(value, ("filename", "file_name", "source"))

            refs: list[str] = []
            if document_id is not None:
                if page_number is not None:
                    refs.append(f"document:{document_id}/page:{page_number}")
                else:
                    refs.append(f"document:{document_id}")

            if filename:
                refs.append(f"file:{filename}")

            return refs

        if not isinstance(value, list):
            return []

        refs: list[str] = []
        for entry in value:
            refs.extend(self._extract_refs(entry))
        return refs

    @staticmethod
    def _extract_int(payload: Mapping[str, Any], key: str) -> int | None:
        value = payload.get(key)
        if isinstance(value, int):
            return value
        if isinstance(value, str):
            try:
                return int(value)
            except ValueError:
                return None
        return None

    @staticmethod
    def _dedupe_preserving_order(values: list[str]) -> list[str]:
        deduped: list[str] = []
        seen: set[str] = set()

        for value in values:
            if value and value not in seen:
                deduped.append(value)
                seen.add(value)

        return deduped

    def _extract_metadata_value(self, payload: Any, key: str) -> str | None:
        if isinstance(payload, Mapping) and isinstance(payload.get("metadata"), Mapping):
            metadata_value = payload["metadata"].get(key)
            if isinstance(metadata_value, str) and metadata_value.strip():
                return metadata_value
        data = payload.get("data") if isinstance(payload, Mapping) else None
        if isinstance(data, Mapping) and isinstance(data.get("metadata"), Mapping):
            metadata_value = data["metadata"].get(key)
            if isinstance(metadata_value, str) and metadata_value.strip():
                return metadata_value
        return None


class LightRagRetriever(_KnowledgeItemFactory, IKnowledgeRetriever):
    """Retrieve knowledge by proxying LightRAG and shaping the response."""

    def __init__(
        self, 
        query_service: LightRagQueryService | None = None,
        neo4j_service: Neo4jService | None = None
    ) -> None:
        self._query_service = query_service or LightRagQueryService()
        self._neo4j_service = neo4j_service

    async def retrieve(
        self,
        query: str,
        *,
        tenant_id: str = "default",
        correlation_id: str | None = None,
        limit: int = 10,
        **options: Any,
    ) -> KnowledgeResult:
        resolved_limit = max(limit, 1)
        payload = LightRagQueryRequest(
            query=query,
            mode=str(options.get("mode", "mix")),
            top_k=max(int(options.get("top_k", resolved_limit)), 1),
            chunk_top_k=max(int(options.get("chunk_top_k", resolved_limit)), 1),
            include_references=bool(options.get("include_references", True)),
            include_chunk_content=bool(options.get("include_chunk_content", True)),
            tenant_id=tenant_id or "default",
            correlation_id=correlation_id,
        )
        response = await asyncio.to_thread(self._query_service.query_data, payload)
        items = self._extract_items(response, payload.top_k)
        resolved_correlation_id = (
            correlation_id
            or self._extract_metadata_value(response, "correlation_id")
            or uuid.uuid4().hex
        )
        resolved_tenant_id = (
            tenant_id
            or self._extract_metadata_value(response, "tenant_id")
            or "default"
        )
        return KnowledgeResult(
            tenant_id=resolved_tenant_id,
            correlation_id=resolved_correlation_id,
            results=items,
        )

    def _extract_items(self, payload: Any, limit: int) -> list[KnowledgeItem]:
        if not isinstance(payload, Mapping):
            return []

        items = self._first_list(payload, ("results", "items", "chunks"))
        if items is None:
            data = payload.get("data")
            if isinstance(data, Mapping):
                items = self._first_list(data, ("results", "items", "chunks"))

        if items is not None:
            return [
                item
                for item in (
                    self._build_item(entry, enrich_confidence=True) for entry in list(items)[: max(limit, 1)]
                )
                if item is not None and item.content
            ]

        data = payload.get("data")
        content = self._first_string(payload, ("response", "answer", "content", "text", "result"))
        if not content and isinstance(data, Mapping):
            content = self._first_string(
                data, ("response", "answer", "content", "text", "result")
            )

        if not content:
            return []

        fallback_confidence = self._first_float(payload, SCORE_KEYS)
        if fallback_confidence is None:
            fallback_confidence = self._first_float(data, SCORE_KEYS)
        
        # NEW: Try to enrich from Neo4j when confidence is missing
        if fallback_confidence is None and self._neo4j_service is not None:
            fallback_confidence = self._enrich_confidence_from_provenance(payload)
            if fallback_confidence is None and isinstance(data, Mapping):
                fallback_confidence = self._enrich_confidence_from_provenance(data)
        
        # If still no confidence, fail closed: return empty list
        # This forces fallback to semantic retriever instead of guessing 0.5
        if fallback_confidence is None:
            return []
        
        fallback_source_refs = self._extract_source_refs(payload)
        if not fallback_source_refs and isinstance(data, Mapping):
            fallback_source_refs = self._extract_source_refs(data)

        return [
            KnowledgeItem(
                content=content,
                confidence=fallback_confidence,
                source_refs=fallback_source_refs,
                relevance_score=fallback_confidence,
            )
        ]


class SemanticKnowledgeRetriever(_KnowledgeItemFactory, IKnowledgeRetriever):
    """Retrieve knowledge from the current Neo4j-backed semantic search surface."""

    def __init__(self, neo4j_service: Neo4jService | None = None) -> None:
        self._neo4j_service = neo4j_service or Neo4jService()

    async def retrieve(
        self,
        query: str,
        *,
        tenant_id: str = "default",
        correlation_id: str | None = None,
        limit: int = 10,
        **options: Any,
    ) -> KnowledgeResult:
        resolved_limit = max(limit, 1)

        document_ids = options.get("document_ids")

        # Try claims first (Validation Layer output)
        claim_results = await asyncio.to_thread(
            self._neo4j_service.search_claims,
            query,
            resolved_limit,
        )

        raw_results = self._filter_results_by_document_ids(claim_results, document_ids)

        # Fall back to page search if no scoped claims found
        if not raw_results:
            page_results = await asyncio.to_thread(
                self._neo4j_service.search_similar_content,
                query,
                resolved_limit,
            )
            raw_results = self._filter_results_by_document_ids(page_results, document_ids)

        items = [
            item
            for item in (
                self._build_item(result, enrich_confidence=False) for result in raw_results[:resolved_limit]
            )
            if item is not None and item.content
        ]

        return KnowledgeResult(
            tenant_id=tenant_id or "default",
            correlation_id=correlation_id or uuid.uuid4().hex,
            results=items,
        )

    @staticmethod
    def _filter_results_by_document_ids(
        results: list[Mapping[str, Any]] | list[dict[str, Any]],
        document_ids: Any,
    ) -> list[Mapping[str, Any]] | list[dict[str, Any]]:
        if not isinstance(document_ids, list) or not document_ids:
            return results

        allowed_ids = {document_id for document_id in document_ids if isinstance(document_id, int)}
        if not allowed_ids:
            return results

        return [
            result
            for result in results
            if isinstance(result, Mapping) and result.get("document_id") in allowed_ids
        ]


class BrainKnowledgeRetriever(IKnowledgeRetriever):
    """Own LightRAG-first retrieval with Neo4j semantic fallback."""

    def __init__(
        self,
        light_rag_retriever: IKnowledgeRetriever | None = None,
        semantic_retriever: IKnowledgeRetriever | None = None,
        neo4j_service: Neo4jService | None = None,
    ) -> None:
        self._light_rag_retriever = light_rag_retriever or LightRagRetriever(neo4j_service=neo4j_service)
        self._semantic_retriever = semantic_retriever or SemanticKnowledgeRetriever(neo4j_service)

    async def retrieve(
        self,
        query: str,
        *,
        tenant_id: str = "default",
        correlation_id: str | None = None,
        limit: int = 10,
        **options: Any,
    ) -> KnowledgeResult:
        resolved_limit = max(limit, 1)
        resolved_tenant_id = tenant_id or "default"
        resolved_correlation_id = correlation_id

        try:
            primary_result = await self._light_rag_retriever.retrieve(
                query,
                tenant_id=resolved_tenant_id,
                correlation_id=correlation_id,
                limit=resolved_limit,
                mode=str(options.get("mode", "mix")),
                top_k=max(int(options.get("top_k", resolved_limit)), 1),
                chunk_top_k=max(int(options.get("chunk_top_k", resolved_limit)), 1),
                include_references=bool(options.get("include_references", True)),
                include_chunk_content=bool(options.get("include_chunk_content", True)),
            )
            resolved_tenant_id = primary_result.tenant_id or resolved_tenant_id
            resolved_correlation_id = primary_result.correlation_id or resolved_correlation_id
            if primary_result.results:
                return primary_result
        except Exception:
            pass

        return await self._semantic_retriever.retrieve(
            query,
            tenant_id=resolved_tenant_id,
            correlation_id=resolved_correlation_id,
            limit=resolved_limit,
            document_ids=options.get("document_ids"),
        )


class LightRAGRetriever(LightRagRetriever):
    """Alias that matches the Phase 2 roadmap and contract naming."""
