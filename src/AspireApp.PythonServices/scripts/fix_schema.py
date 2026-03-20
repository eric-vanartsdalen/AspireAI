#!/usr/bin/env python3
"""
Verify and seed the canonical AspireAI Python database footprint.
"""

from __future__ import annotations

import argparse
import sqlite3
import sys
from pathlib import Path

sys.path.append(str(Path(__file__).parent.parent))

from app.services.database_service import DatabaseService


def check_schema_status() -> dict | None:
    """Check whether the canonical tables exist and report row counts."""
    print("🔎 Checking database schema status...")
    try:
        db_service = DatabaseService()
        health = db_service.health_check()
        print(f"✅ Database health: {health.get('status', 'unknown')}")

        with sqlite3.connect(db_service.db_path) as conn:
            tables = {
                row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")
            }
            files_count = conn.execute("SELECT COUNT(*) FROM files").fetchone()[0] if "files" in tables else 0
            pages_count = (
                conn.execute("SELECT COUNT(*) FROM document_pages").fetchone()[0]
                if "document_pages" in tables
                else 0
            )

        legacy_tables = sorted({"documents", "processed_documents"} & tables)
        status = {
            "database_path": db_service.db_path,
            "tables": sorted(tables),
            "files_count": files_count,
            "document_pages_count": pages_count,
            "legacy_tables": legacy_tables,
            "healthy": {"files", "document_pages"}.issubset(tables),
        }

        print(f"📍 Database path: {status['database_path']}")
        print(f"📋 Tables: {status['tables']}")
        print(f"📄 files rows: {status['files_count']}")
        print(f"📄 document_pages rows: {status['document_pages_count']}")
        if legacy_tables:
            print(f"⚠️ Legacy tables present: {legacy_tables}")

        return status
    except Exception as e:
        print(f"❌ Error checking schema: {e}")
        return None


def seed_sample_data() -> bool:
    """Create one canonical sample row to verify write operations."""
    print("\n🧪 Seeding canonical sample data...")
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
        print(f"✅ Sample file lifecycle succeeded for file {file_id}")
        return True
    except Exception as e:
        print(f"❌ Error creating sample data: {e}")
        return False


def show_table_info() -> bool:
    """Show detailed information about database tables."""
    print("\n📚 Database Table Information:")
    try:
        db_service = DatabaseService()
        with sqlite3.connect(db_service.db_path) as conn:
            tables = conn.execute(
                "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
            ).fetchall()

            print(f"\n📋 Tables found: {len(tables)}")
            for (table_name,) in tables:
                print(f"\n   • {table_name}:")
                columns = conn.execute(f"PRAGMA table_info({table_name})").fetchall()
                for col_id, name, col_type, not_null, default, pk in columns:
                    pk_marker = " (PK)" if pk else ""
                    null_marker = " NOT NULL" if not_null else ""
                    default_info = f" DEFAULT {default}" if default else ""
                    print(f"      - {name}: {col_type}{pk_marker}{null_marker}{default_info}")
                count = conn.execute(f"SELECT COUNT(*) FROM {table_name}").fetchone()[0]
                print(f"      rows: {count}")
        return True
    except Exception as e:
        print(f"❌ Error getting table info: {e}")
        return False


def main() -> int:
    print("🛠️ AspireAI Canonical Schema Tool")
    print("=" * 50)

    status = check_schema_status()
    if not status:
        return 1

    show_table_info()

    if not status["healthy"]:
        print("\n❌ Canonical tables are missing.")
        return 1

    if not seed_sample_data():
        return 1

    print("\n✅ Canonical schema is healthy and writable.")
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Verify the AspireAI canonical Python schema")
    parser.add_argument("--check-only", action="store_true", help="Only check schema status")
    parser.add_argument("--show-tables", action="store_true", help="Show detailed table information")
    args = parser.parse_args()

    if args.check_only:
        raise SystemExit(0 if check_schema_status() else 1)
    if args.show_tables:
        raise SystemExit(0 if show_table_info() else 1)
    raise SystemExit(main())
