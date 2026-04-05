import json
from pathlib import Path
from typing import Any, Dict, Iterable


def sanitize_file_stem(filename: str) -> str:
    """Create a filesystem-safe file stem while preserving readability."""
    raw_stem = Path(filename).stem or "document"
    cleaned = "".join(
        character if character.isalnum() or character in {"-", "_"} else "-"
        for character in raw_stem
    )
    compact = "-".join(part for part in cleaned.split("-") if part)
    return (compact[:80] or "document").lower()


def export_docling_outputs(
    docling_document: Any,
    output_dir: Path,
    base_filename: str,
    include_json: bool = True,
    include_html: bool = True,
) -> Dict[str, str]:
    """Persist Docling exports in formats that are easy to inspect and reuse."""
    output_dir.mkdir(parents=True, exist_ok=True)
    export_paths: Dict[str, str] = {}

    markdown_content = ""
    if hasattr(docling_document, "export_to_markdown"):
        markdown_content = docling_document.export_to_markdown() or ""

    markdown_path = output_dir / f"{base_filename}.md"
    markdown_path.write_text(markdown_content, encoding="utf-8")
    export_paths["markdown"] = str(markdown_path)

    if include_json:
        json_path = output_dir / f"{base_filename}.json"
        document_payload = _get_serializable_docling_payload(docling_document, base_filename)
        json_path.write_text(
            json.dumps(document_payload, indent=2, ensure_ascii=False, default=str),
            encoding="utf-8",
        )
        export_paths["json"] = str(json_path)

    if include_html and hasattr(docling_document, "export_to_html"):
        html_path = output_dir / f"{base_filename}.html"
        html_path.write_text(docling_document.export_to_html() or "", encoding="utf-8")
        export_paths["html"] = str(html_path)

    return export_paths


def build_markdown_from_pages(title: str, pages: Iterable[Any]) -> str:
    """Build a simple markdown document from extracted page content."""
    lines = [f"# {title}", ""]

    for page in sorted(pages, key=lambda current: current.page_number):
        lines.extend(
            [
                f"## Page {page.page_number}",
                "",
                (page.content or "").strip() or "_No extracted text._",
                "",
            ]
        )

    return "\n".join(lines).strip() + "\n"


def write_markdown_output(output_dir: Path, base_filename: str, markdown_content: str) -> str:
    """Persist markdown content and return the file path as a string."""
    output_dir.mkdir(parents=True, exist_ok=True)
    markdown_path = output_dir / f"{base_filename}.md"
    markdown_path.write_text(markdown_content, encoding="utf-8")
    return str(markdown_path)


def _get_serializable_docling_payload(docling_document: Any, base_filename: str) -> Dict[str, Any]:
    if hasattr(docling_document, "export_to_dict"):
        return docling_document.export_to_dict()

    if hasattr(docling_document, "to_dict"):
        return docling_document.to_dict()

    if hasattr(docling_document, "dict"):
        return docling_document.dict()

    return {
        "filename": base_filename,
        "note": "Docling document did not expose a JSON export helper.",
    }
