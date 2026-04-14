import json
import os
import shutil
import time
from pathlib import Path
from typing import Any, Dict, List
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
    ):
        data_root = Path(os.getenv("ASPIRE_DATA_PATH", "/app/data"))
        configured_input_dir = input_dir or os.getenv("LIGHTRAG_INPUT_DIR")
        configured_service_url = service_url if service_url is not None else os.getenv("LIGHTRAG_URL")

        self.input_dir = Path(configured_input_dir) if configured_input_dir else data_root / "inputs"
        self.service_url = (configured_service_url or "http://lightrag:9621").rstrip("/")
        self.scan_timeout_seconds = scan_timeout_seconds

    def handoff_document(self, document: Document, markdown_path: str | Path) -> Dict[str, Any]:
        staged_path = self.stage_markdown(document, markdown_path)
        scan_response = self.trigger_scan()

        return {
            "staged_input_path": str(staged_path),
            "scan_response": scan_response,
            "scan_requested": True,
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
        documents_response = self._json_request("GET", "/documents")
        statuses = documents_response.get("statuses", {})
        doc_ids: List[str] = []

        for documents in statuses.values():
            if not isinstance(documents, list):
                continue

            for document in documents:
                current_file_path = Path(str(document.get("file_path", ""))).name.lower()
                current_doc_id = document.get("id")
                if current_file_path == target_name and current_doc_id:
                    doc_ids.append(str(current_doc_id))

        return doc_ids

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
        documents_response = self._json_request("GET", "/documents")
        statuses = documents_response.get("statuses", {})
        doc_ids: set[str] = set()

        for documents in statuses.values():
            if not isinstance(documents, list):
                continue

            for document in documents:
                current_doc_id = document.get("id")
                if current_doc_id:
                    doc_ids.add(str(current_doc_id))

        return doc_ids

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
