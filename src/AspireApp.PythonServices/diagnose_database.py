#!/usr/bin/env python3
"""
Diagnostic helper for the shared PostgreSQL operational document store.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from app.services.database_service import DatabaseService


def main() -> int:
    print("AspireAI Operational Store Diagnostic")
    print("=" * 40)

    print(f"POSTGRES_HOST={os.getenv('POSTGRES_HOST', 'not_set')}")
    print(f"POSTGRES_PORT={os.getenv('POSTGRES_PORT', 'not_set')}")
    print(
        "POSTGRES_DATABASE="
        f"{os.getenv('POSTGRES_DATABASE', os.getenv('POSTGRES_DB', 'not_set'))}"
    )

    try:
        db_service = DatabaseService()
        health = db_service.health_check()
        schema = db_service.get_schema_snapshot()
    except Exception as exc:
        print(f"Connection failed: {exc}")
        return 1

    print(f"Status: {health.get('status')}")
    print(f"Target: {health.get('database_target')}")
    print(f"Tables: {schema.get('tables', [])}")
    print(f"Files rows: {schema.get('files_count', 0)}")
    print(f"Document pages rows: {schema.get('document_pages_count', 0)}")
    return 0 if health.get("status") == "healthy" else 1


if __name__ == "__main__":
    raise SystemExit(main())
