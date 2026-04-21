import json
import os
import shutil
import time
from pathlib import Path
from typing import Any, Callable, Dict, Iterable, List
from urllib import error, request

from ..models.models import Document
from .docling_export_service import sanitize_file_stem


class LightRagHandoffService:
    """Stage markdown in LightRAG INPUT_DIR and trigger the documented scan API."""

    def __init__(
        self,
        input_dir: str | Path | None = None,
        service_url: str | None = None,
        scan_timeout_seconds: float = 15.0,
        readiness_timeout_seconds: float = 300.0,
        readiness_poll_interval_seconds: float = 1.0,
        doc_status_store_path: str | Path | None = None,
    ):
        data_root = Path(os.getenv("ASPIRE_DATA_PATH", "/app/data"))
        configured_input_dir = input_dir or os.getenv("LIGHTRAG_INPUT_DIR")
        configured_service_url = service_url if service_url is not None else os.getenv("LIGHTRAG_URL")
        configured_doc_status_store_path = doc_status_store_path or os.getenv("LIGHTRAG_DOC_STATUS_PATH")

        self.input_dir = Path(configured_input_dir) if configured_input_dir else data_root / "inputs"
        self.service_url = (configured_service_url or "http://lightrag:9621").rstrip("/")
        self.scan_timeout_seconds = scan_timeout_seconds
        self.readiness_timeout_seconds = readiness_timeout_seconds
        self.readiness_poll_interval_seconds = readiness_poll_interval_seconds
        self.doc_status_store_path = (
            Path(configured_doc_status_store_path)
            if configured_doc_status_store_path
            else data_root / "rag_storage" / "kv_store_doc_status.json"
        )

    def handoff_document(self, document: Document, markdown_path: str | Path) -> Dict[str, Any]:
        staged_path = self.stage_markdown(document, markdown_path)
        scan_response = self.trigger_scan()

        return {
            "staged_input_path": str(staged_path),
            "scan_response": scan_response,
            "scan_requested": True,
            "indexing_status": "queued",
        }

    def stage_markdown(self, document: Document, markdown_path: str | Path) -> Path:
        source_path = Path(markdown_path)
        if not source_path.exists():
            raise FileNotFoundError(f"LightRAG handoff source does not exist: {source_path}")

        self.input_dir.mkdir(parents=True, exist_ok=True)
        staged_path = self.build_staged_path(document)
        shutil.copyfile(source_path, staged_path)
        return staged_path

    def build_staged_path(self, document: Document) -> Path:
        staged_filename = f"{document.id:06d}-{sanitize_file_stem(document.original_filename or document.filename)}.md"
        return self.input_dir / staged_filename

    def trigger_scan(self) -> Dict[str, Any]:
        self.wait_for_service_ready(max(self.scan_timeout_seconds, 30.0))
        return self._json_request("POST", "/documents/scan", payload={})

    def cleanup_document(
        self,
        document: Document,
        staged_input_path: str | Path | None = None,
        delete_llm_cache: bool = False,
        wait_timeout_seconds: float = 30.0,
    ) -> Dict[str, Any]:
        staged_path = Path(staged_input_path) if staged_input_path else self.build_staged_path(document)
        doc_ids = self.find_document_ids_by_file_path(staged_path.name)
        delete_response: Dict[str, Any] | None = None

        if doc_ids:
            delete_response = self.request_document_delete(
                doc_ids,
                delete_file=True,
                delete_llm_cache=delete_llm_cache,
            )
            self.wait_for_document_removal(doc_ids, wait_timeout_seconds)

        removed_paths = self.remove_staged_inputs(staged_path)

        return {
            "doc_ids": doc_ids,
            "delete_response": delete_response,
            "removed_paths": removed_paths,
        }

    def find_document_ids_by_file_path(self, file_path: str | Path) -> List[str]:
        target_name = Path(str(file_path)).name.lower()
        doc_ids: List[str] = []

        for document in self._iter_document_status_entries(self._json_request("GET", "/documents")):
            current_file_path = Path(str(document.get("file_path", ""))).name.lower()
            current_doc_id = document.get("id")
            if current_file_path == target_name and current_doc_id:
                doc_ids.append(str(current_doc_id))

        return doc_ids

    def wait_for_document_readiness(
        self,
        staged_input_path: str | Path,
        *,
        timeout_seconds: float | None = None,
        poll_interval_seconds: float | None = None,
        status_callback: Callable[[str, str | None], None] | None = None,
    ) -> Dict[str, Any]:
        target_name = Path(str(staged_input_path)).name.lower()
        effective_timeout = timeout_seconds or self.readiness_timeout_seconds
        effective_poll_interval = poll_interval_seconds or self.readiness_poll_interval_seconds
        deadline = time.monotonic() + effective_timeout
        current_status = "queued"
        last_observed_entry: Dict[str, Any] | None = None
        last_poll_error: str | None = None

        self._emit_status(status_callback, current_status, None)

        while time.monotonic() < deadline:
            try:
                document_status = self.get_document_status_by_file_path(target_name)
            except Exception as exc:
                last_poll_error = str(exc)
            else:
                last_poll_error = None
                if document_status is not None:
                    last_observed_entry = document_status
                    observed_status = self._normalize_document_status(
                        str(document_status.get("status") or document_status.get("status_group") or "")
                    )
                    observed_error = self._extract_document_error(document_status)

                    if observed_status and observed_status != current_status:
                        current_status = observed_status
                        self._emit_status(
                            status_callback,
                            current_status,
                            observed_error if current_status in {"failed", "timed_out"} else None,
                        )

                    if current_status in {"ready", "failed"}:
                        return {
                            "indexing_status": current_status,
                            "indexing_error": observed_error,
                            "lightrag_document_id": document_status.get("id"),
                            "document_status": document_status,
                        }

            time.sleep(effective_poll_interval)

        timeout_message = (
            f"Timed out after {effective_timeout:.0f}s waiting for LightRAG readiness for {target_name}"
        )
        if last_poll_error:
            timeout_message = f"{timeout_message}: {last_poll_error}"

        last_known_status = current_status if current_status in {"queued", "indexing"} else None
        readiness_deferred = last_known_status is not None
        if not readiness_deferred:
            self._emit_status(status_callback, "timed_out", timeout_message)
        return {
            "indexing_status": "timed_out",
            "indexing_error": timeout_message,
            "lightrag_document_id": last_observed_entry.get("id") if last_observed_entry else None,
            "document_status": last_observed_entry,
            "readiness_deferred": readiness_deferred,
            "last_known_indexing_status": last_known_status,
            "readiness_timeout_message": timeout_message,
        }

    def request_document_delete(
        self,
        doc_ids: List[str],
        *,
        delete_file: bool,
        delete_llm_cache: bool,
    ) -> Dict[str, Any]:
        if not doc_ids:
            return {"status": "not_requested"}

        timeout_seconds = max(self.scan_timeout_seconds, 30.0)
        deadline = time.monotonic() + timeout_seconds

        while True:
            self.wait_for_pipeline_idle(max(deadline - time.monotonic(), 0.5))
            response = self._json_request(
                "DELETE",
                "/documents/delete_document",
                payload={
                    "doc_ids": doc_ids,
                    "delete_file": delete_file,
                    "delete_llm_cache": delete_llm_cache,
                },
                timeout=timeout_seconds,
            )

            status = str(response.get("status", ""))
            if status == "deletion_started":
                return response

            if status == "busy" and time.monotonic() < deadline:
                time.sleep(0.5)
                continue

            if status not in {"busy", "not_allowed"}:
                raise RuntimeError(f"Unexpected LightRAG delete response: {response}")

            raise RuntimeError(f"LightRAG refused delete request: {response}")

    def wait_for_document_removal(
        self,
        doc_ids: List[str],
        timeout_seconds: float,
        poll_interval_seconds: float = 0.5,
    ) -> None:
        deadline = time.monotonic() + timeout_seconds
        target_ids = set(doc_ids)

        while time.monotonic() < deadline:
            remaining_ids = target_ids.intersection(self._list_document_ids())
            if not remaining_ids:
                return

            time.sleep(poll_interval_seconds)

        raise TimeoutError(
            f"Timed out waiting for LightRAG to remove documents: {', '.join(sorted(target_ids))}"
        )

    def remove_staged_inputs(self, staged_input_path: str | Path) -> List[str]:
        staged_path = Path(staged_input_path)
        candidate_paths = [
            staged_path if staged_path.is_absolute() else self.input_dir / staged_path.name,
        ]
        candidate_paths.append(candidate_paths[0].parent / "__enqueued__" / candidate_paths[0].name)

        removed_paths: List[str] = []
        for candidate in candidate_paths:
            if candidate.exists():
                candidate.unlink()
                removed_paths.append(str(candidate))

        return removed_paths

    def wait_for_pipeline_idle(
        self,
        timeout_seconds: float,
        poll_interval_seconds: float = 0.5,
    ) -> None:
        deadline = time.monotonic() + timeout_seconds

        while time.monotonic() < deadline:
            pipeline_status = self._json_request(
                "GET",
                "/documents/pipeline_status",
                timeout=max(self.scan_timeout_seconds, 5.0),
            )
            if not bool(pipeline_status.get("busy", False)):
                return

            time.sleep(poll_interval_seconds)

        raise TimeoutError("Timed out waiting for LightRAG pipeline to become idle")

    def wait_for_service_ready(
        self,
        timeout_seconds: float,
        poll_interval_seconds: float = 0.5,
    ) -> None:
        deadline = time.monotonic() + timeout_seconds
        last_error: Exception | None = None

        while time.monotonic() < deadline:
            try:
                self._json_request(
                    "GET",
                    "/documents",
                    timeout=max(min(self.scan_timeout_seconds, 5.0), 1.0),
                )
                return
            except RuntimeError as exc:
                last_error = exc
                time.sleep(poll_interval_seconds)

        if last_error is not None:
            raise TimeoutError("Timed out waiting for LightRAG service readiness") from last_error

        raise TimeoutError("Timed out waiting for LightRAG service readiness")

    def _list_document_ids(self) -> set[str]:
        doc_ids: set[str] = set()

        for document in self._iter_document_status_entries(self._json_request("GET", "/documents")):
            current_doc_id = document.get("id")
            if current_doc_id:
                doc_ids.add(str(current_doc_id))

        return doc_ids

    def get_document_status_by_file_path(self, file_path: str | Path) -> Dict[str, Any] | None:
        target_name = Path(str(file_path)).name.lower()
        http_error: Exception | None = None
        http_status: Dict[str, Any] | None = None

        try:
            for document in self._iter_document_status_entries(self._json_request("GET", "/documents")):
                current_file_path = Path(str(document.get("file_path", ""))).name.lower()
                if current_file_path == target_name:
                    http_status = document
                    break
        except Exception as exc:
            http_error = exc

        local_status = self._get_local_document_status_by_file_path(target_name)
        selected_status = self._select_best_document_status(http_status, local_status)
        if selected_status is not None:
            return selected_status

        if http_error is not None:
            raise http_error

        return None

    def _iter_document_status_entries(self, documents_response: Dict[str, Any]) -> Iterable[Dict[str, Any]]:
        statuses = documents_response.get("statuses", {})
        if not isinstance(statuses, dict):
            return []

        entries: list[Dict[str, Any]] = []
        for status_group, documents in statuses.items():
            if not isinstance(documents, list):
                continue

            for document in documents:
                if not isinstance(document, dict):
                    continue

                entry = dict(document)
                entry["status_group"] = status_group
                entry.setdefault("status", status_group)
                entries.append(entry)

        return entries

    def _get_local_document_status_by_file_path(self, file_path: str | Path) -> Dict[str, Any] | None:
        target_name = Path(str(file_path)).name.lower()
        if not self.doc_status_store_path.exists():
            return None

        try:
            raw_payload = json.loads(self.doc_status_store_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return None

        if not isinstance(raw_payload, dict):
            return None

        for document in raw_payload.values():
            if not isinstance(document, dict):
                continue

            current_file_path = Path(str(document.get("file_path", ""))).name.lower()
            if current_file_path != target_name:
                continue

            entry = dict(document)
            status_group = str(entry.get("status") or "").strip().lower()
            entry.setdefault("status_group", status_group)
            return entry

        return None

    def _select_best_document_status(
        self,
        primary_status: Dict[str, Any] | None,
        secondary_status: Dict[str, Any] | None,
    ) -> Dict[str, Any] | None:
        if primary_status is None:
            return secondary_status
        if secondary_status is None:
            return primary_status

        primary_priority = self._get_document_status_priority(primary_status)
        secondary_priority = self._get_document_status_priority(secondary_status)
        return secondary_status if secondary_priority > primary_priority else primary_status

    def _get_document_status_priority(self, document_status: Dict[str, Any]) -> int:
        normalized_status = self._normalize_document_status(
            str(document_status.get("status") or document_status.get("status_group") or "")
        )
        if normalized_status in {"ready", "failed"}:
            return 3
        if normalized_status == "indexing":
            return 2
        if normalized_status == "queued":
            return 1
        return 0

    def _normalize_document_status(self, status: str) -> str:
        normalized = (status or "").strip().lower().replace("-", "_").replace(" ", "_")
        if normalized in {"processed", "completed", "complete", "ready"}:
            return "ready"
        if normalized in {"failed", "error"} or "fail" in normalized:
            return "failed"
        if normalized in {"queued", "pending", "enqueued", "waiting"} or "queue" in normalized:
            return "queued"
        if normalized in {"processing", "indexing", "scanning", "ingesting"}:
            return "indexing"
        return "indexing" if normalized else "queued"

    def _extract_document_error(self, document_status: Dict[str, Any]) -> str | None:
        for key in ("error_msg", "error", "message"):
            value = document_status.get(key)
            if isinstance(value, str) and value.strip():
                return value.strip()
        return None

    def _emit_status(
        self,
        status_callback: Callable[[str, str | None], None] | None,
        status: str,
        error: str | None,
    ) -> None:
        if status_callback is not None:
            status_callback(status, error)

    def _json_request(
        self,
        method: str,
        path: str,
        payload: Dict[str, Any] | None = None,
        timeout: float | None = None,
    ) -> Dict[str, Any]:
        request_body = None
        headers = {}
        if payload is not None:
            request_body = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"

        api_request = request.Request(
            f"{self.service_url}{path}",
            data=request_body,
            headers=headers,
            method=method,
        )

        try:
            with request.urlopen(api_request, timeout=timeout or self.scan_timeout_seconds) as response:
                raw_body = response.read().decode("utf-8").strip()
        except error.HTTPError as exc:
            response_body = exc.read().decode("utf-8", errors="ignore").strip()
            raise RuntimeError(
                f"LightRAG request {method} {path} failed with HTTP {exc.code}: {response_body or exc.reason}"
            ) from exc
        except error.URLError as exc:
            raise RuntimeError(
                f"LightRAG request {method} {path} could not reach {self.service_url}: {exc.reason}"
            ) from exc

        if not raw_body:
            return {}

        try:
            return json.loads(raw_body)
        except json.JSONDecodeError:
            return {"raw_response": raw_body}
