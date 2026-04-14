from __future__ import annotations

import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEST_ROOT = Path(__file__).resolve().parent

sys.path = [path for path in sys.path if path != str(PROJECT_ROOT)]
sys.path.insert(0, str(PROJECT_ROOT))
if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

from app.contracts import CanonicalDocument, PageContent


def build_sample_canonical_document() -> CanonicalDocument:
    return CanonicalDocument(
        tenant_id="tenant-roundtrip",
        correlation_id="corr-roundtrip",
        document_id=42,
        source_type="upload",
        source_confidence=0.9,
        pages=[
            PageContent(
                page_number=1,
                content="Round-trip page one",
                section="overview",
                metadata={"language": "en"},
            )
        ],
        metadata={"category": "science", "origin": "python"},
    )


def emit_canonical() -> int:
    print(build_sample_canonical_document().model_dump_json())
    return 0


def validate_canonical(path: str) -> int:
    payload = Path(path).read_text(encoding="utf-8")
    document = CanonicalDocument.model_validate_json(payload)
    print(document.model_dump_json())
    return 0


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        raise SystemExit("Expected a command: emit-canonical or validate-canonical <path>")

    command = argv[1]
    if command == "emit-canonical":
        return emit_canonical()

    if command == "validate-canonical":
        if len(argv) != 3:
            raise SystemExit("validate-canonical requires a path to a JSON payload")
        return validate_canonical(argv[2])

    raise SystemExit(f"Unknown command: {command}")


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
