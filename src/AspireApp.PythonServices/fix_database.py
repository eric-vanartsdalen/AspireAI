#!/usr/bin/env python3
"""
Create or repair a working database using the canonical Python schema.
"""

from __future__ import annotations

import os
import sqlite3
from pathlib import Path

from app.services.database_service import DatabaseService


def create_working_database() -> str:
    """Create a working database in a writable location using DatabaseService."""
    db_locations = [
        "/app/database/data-resources.db",
        "/tmp/aspire_database/data-resources.db",
        "/tmp/data-resources.db",
    ]

    for db_path in db_locations:
        try:
            path = Path(db_path)
            print(f"🔧 Trying database path: {path}")
            path.parent.mkdir(parents=True, exist_ok=True)

            service = DatabaseService(db_path=str(path))
            health = service.health_check()
            if health.get("status") != "healthy":
                raise RuntimeError(f"Health check failed: {health}")

            with sqlite3.connect(path) as conn:
                tables = {
                    row[0]
                    for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")
                }

            if not {"files", "document_pages"}.issubset(tables):
                raise RuntimeError(f"Canonical tables missing at {path}: {tables}")

            os.environ["ASPIRE_DB_PATH"] = str(path)
            print(f"✅ Database ready at: {path}")
            if {"documents", "processed_documents"} & tables:
                print("⚠️ Legacy tables are present but not used by the live Python service.")
            return str(path)
        except Exception as e:
            print(f"❌ Failed to prepare database at {db_path}: {e}")

    raise RuntimeError("Could not create a canonical database at any candidate path")


if __name__ == "__main__":
    try:
        working_db = create_working_database()
        print(f"\n✅ Python service can use: {working_db}")
    except Exception as e:
        print(f"\n❌ Database creation failed: {e}")
        raise SystemExit(1)
