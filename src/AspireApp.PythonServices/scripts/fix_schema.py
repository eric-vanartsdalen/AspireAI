#!/usr/bin/env python3
"""
Verify the canonical PostgreSQL schema and exercise a sample file lifecycle.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

sys.path.append(str(Path(__file__).parent.parent))

from app.services.database_service import DatabaseService


def check_schema_status() -> dict | None:
    print("Checking PostgreSQL schema status...")
    try:
        db_service = DatabaseService()
        health = db_service.health_check()
        schema = db_service.get_schema_snapshot()
        print(f"Database target: {health.get('database_target')}")
        print(f"Health: {health.get('status')}")
        print(f"Tables: {schema.get('tables', [])}")
        print(f"files rows: {schema.get('files_count', 0)}")
        print(f"document_pages rows: {schema.get('document_pages_count', 0)}")
        return schema
    except Exception as exc:
        print(f"Schema check failed: {exc}")
        return None


def seed_sample_data() -> bool:
    print("\nSeeding canonical sample data...")
    try:
        db_service = DatabaseService()
        file_id = db_service.create_file_record(
            file_name="schema-check.pdf",
            original_file_name="schema-check.pdf",
            file_path="uploads",
            file_size=1024,
            mime_type="application/pdf",
            status="uploaded",
        )
        db_service.update_file_status(file_id, "processing")
        db_service.update_file_processing_results(
            file_id=file_id,
            docling_path="/app/data/processed/documents/schema-check/document.json",
            total_pages=1,
        )
        db_service.save_document_page(
            file_id=file_id,
            page_number=1,
            content="schema check",
            metadata={"source": "fix_schema.py"},
        )
        db_service.update_file_status(file_id, "processed")
        print(f"Sample lifecycle succeeded for file {file_id}")
        return True
    except Exception as exc:
        print(f"Sample lifecycle failed: {exc}")
        return False


def main() -> int:
    print("AspireAI PostgreSQL Schema Tool")
    print("=" * 40)
    status = check_schema_status()
    if not status:
        return 1

    required_tables = {"files", "document_pages"}
    if not required_tables.issubset(set(status.get("tables", []))):
        print("Canonical tables are missing.")
        return 1

    return 0 if seed_sample_data() else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Verify the AspireAI PostgreSQL schema")
    parser.add_argument("--check-only", action="store_true", help="Only validate the schema")
    args = parser.parse_args()

    if args.check_only:
        raise SystemExit(0 if check_schema_status() else 1)
    raise SystemExit(main())
