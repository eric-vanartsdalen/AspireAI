import json
import os
from typing import Any, Dict
from urllib import error, request

from ..models.models import LightRagQueryRequest


class LightRagQueryService:
    """Query LightRAG through the Python retrieval layer."""

    def __init__(
        self,
        service_url: str | None = None,
        query_timeout_seconds: float = 60.0,
    ):
        configured_service_url = service_url if service_url is not None else os.getenv("LIGHTRAG_URL")
        self.service_url = (configured_service_url or "http://lightrag:9621").rstrip("/")
        self.query_timeout_seconds = query_timeout_seconds

    def query_data(self, query_request: LightRagQueryRequest | Dict[str, Any]) -> Dict[str, Any]:
        payload = self._serialize_query_request(query_request)
        payload.setdefault("include_references", True)
        payload.setdefault("include_chunk_content", True)
        return self._post_json("/query/data", payload)

    def _post_json(self, relative_path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        encoded_payload = json.dumps(payload).encode("utf-8")
        query_request = request.Request(
            f"{self.service_url}{relative_path}",
            data=encoded_payload,
            method="POST",
            headers={"Content-Type": "application/json"},
        )

        try:
            with request.urlopen(query_request, timeout=self.query_timeout_seconds) as response:
                response_body = response.read().decode("utf-8").strip()
        except error.HTTPError as exc:
            response_body = exc.read().decode("utf-8", errors="ignore").strip()
            raise RuntimeError(
                f"LightRAG query failed with HTTP {exc.code}: {response_body or exc.reason}"
            ) from exc
        except error.URLError as exc:
            raise RuntimeError(f"LightRAG query could not reach {self.service_url}: {exc.reason}") from exc

        if not response_body:
            raise RuntimeError("LightRAG query returned an empty response body.")

        try:
            return json.loads(response_body)
        except json.JSONDecodeError as exc:
            raise RuntimeError(f"LightRAG query returned invalid JSON: {response_body}") from exc

    @staticmethod
    def _serialize_query_request(query_request: LightRagQueryRequest | Dict[str, Any]) -> Dict[str, Any]:
        if isinstance(query_request, dict):
            payload = dict(query_request)
            payload.pop("tenant_id", None)
            payload.pop("correlation_id", None)
            return payload

        return query_request.dict(exclude={"tenant_id", "correlation_id"}, exclude_none=True)
