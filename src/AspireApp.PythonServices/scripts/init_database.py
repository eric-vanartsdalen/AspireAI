#!/usr/bin/env python3
"""
Initialize and verify the shared PostgreSQL operational store.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from app.services.database_service import DatabaseService


def main() -> int:
    print("=== AspireAI PostgreSQL Initialization ===")
    try:
        db_service = DatabaseService()
        health = db_service.health_check()
        schema = db_service.get_schema_snapshot()
    except Exception as exc:
        print(f"Initialization failed: {exc}")
        return 1

    print(f"Target: {health.get('database_target')}")
    print(f"Status: {health.get('status')}")
    print(f"Files rows: {schema.get('files_count', 0)}")
    print(f"Document pages rows: {schema.get('document_pages_count', 0)}")
    print(f"Tables: {', '.join(schema.get('tables', []))}")

    data_root = Path(__file__).resolve().parents[1] / "app"
    print(f"Python service root: {data_root}")
    return 0 if health.get("status") == "healthy" else 1


if __name__ == "__main__":
    raise SystemExit(main())
