#!/usr/bin/env python3
"""
Database diagnostic tool for the canonical AspireAI Python footprint.
"""

from __future__ import annotations

import os
import sqlite3
import sys
from pathlib import Path


CANONICAL_TABLES = ("files", "document_pages")
LEGACY_TABLES = ("documents", "processed_documents")


def check_database_directory(db_path: Path) -> bool:
    """Check whether the database directory exists and is writable."""
    db_dir = db_path.parent
    print(f"Database path: {db_path}")
    print(f"Database directory: {db_dir}")
    print(f"Database file exists: {db_path.exists()}")
    print(f"Database directory exists: {db_dir.exists()}")

    if not db_dir.exists():
        print("❌ Database directory does not exist")
        return False

    print(f"Directory is readable: {os.access(db_dir, os.R_OK)}")
    print(f"Directory is writable: {os.access(db_dir, os.W_OK)}")
    print(f"Directory is executable: {os.access(db_dir, os.X_OK)}")
    if db_path.exists():
        print(f"File is readable: {os.access(db_path, os.R_OK)}")
        print(f"File is writable: {os.access(db_path, os.W_OK)}")
        print(f"File size: {db_path.stat().st_size} bytes")
    return True


def inspect_database_schema(db_path: Path) -> bool:
    """Inspect canonical and legacy tables."""
    try:
        with sqlite3.connect(db_path) as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
            tables = {row[0] for row in cursor.fetchall()}

            print(f"Existing tables: {sorted(tables)}")
            missing = sorted(set(CANONICAL_TABLES) - tables)
            if missing:
                print(f"❌ Missing canonical tables: {missing}")
                return False

            legacy = sorted(set(LEGACY_TABLES) & tables)
            if legacy:
                print(f"⚠️ Legacy tables still present: {legacy}")
                print("   They are no longer part of the supported Python footprint.")

            for table in CANONICAL_TABLES:
                cursor.execute(f"PRAGMA table_info({table})")
                columns = cursor.fetchall()
                print(f"\n📋 {table} columns:")
                for column in columns:
                    print(f"  - {column[1]} ({column[2]})")

            return True
    except Exception as e:
        print(f"❌ Schema check failed: {e}")
        return False


def main() -> None:
    print("🩺 AspireAI Database Diagnostic Tool")
    print("=" * 50)

    db_path = Path(os.environ.get("ASPIRE_DB_PATH", "/app/database/data-resources.db"))
    if not check_database_directory(db_path):
        sys.exit(1)

    if db_path.exists() and inspect_database_schema(db_path):
        print("\n✅ Database schema looks compatible with the live Python service.")
        sys.exit(0)

    print("\n❌ Database schema is missing the canonical Python tables.")
    sys.exit(1)


if __name__ == "__main__":
    main()
