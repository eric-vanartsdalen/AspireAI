import json
import os
import shutil
from pathlib import Path
from typing import Any, Dict
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
        staged_filename = f"{document.id:06d}-{sanitize_file_stem(document.original_filename or document.filename)}.md"
        staged_path = self.input_dir / staged_filename
        shutil.copyfile(source_path, staged_path)
        return staged_path

    def trigger_scan(self) -> Dict[str, Any]:
        scan_request = request.Request(
            f"{self.service_url}/documents/scan",
            data=b"",
            method="POST",
        )

        try:
            with request.urlopen(scan_request, timeout=self.scan_timeout_seconds) as response:
                raw_body = response.read().decode("utf-8").strip()
        except error.HTTPError as exc:
            response_body = exc.read().decode("utf-8", errors="ignore").strip()
            raise RuntimeError(
                f"LightRAG scan failed with HTTP {exc.code}: {response_body or exc.reason}"
            ) from exc
        except error.URLError as exc:
            raise RuntimeError(f"LightRAG scan could not reach {self.service_url}: {exc.reason}") from exc

        if not raw_body:
            return {"status": "scanning_started"}

        try:
            return json.loads(raw_body)
        except json.JSONDecodeError:
            return {"status": "scanning_started", "raw_response": raw_body}
