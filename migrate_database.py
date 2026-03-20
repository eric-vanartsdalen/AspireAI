#!/usr/bin/env python3
"""
Canonical database footprint helper for AspireAI.

Ensures the Python-facing `files` and `document_pages` tables exist and can optionally
remove legacy compatibility tables that are no longer part of the supported contract.
"""

from __future__ import annotations

import argparse
import shutil
import sqlite3
from datetime import datetime
from pathlib import Path


FILES_SCHEMA = """
CREATE TABLE IF NOT EXISTS files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_name TEXT NOT NULL,
    original_file_name TEXT NOT NULL,
    file_path TEXT NOT NULL,
    file_hash TEXT NOT NULL DEFAULT '',
    file_size INTEGER NOT NULL DEFAULT 0,
    mime_type TEXT,
    uploaded_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    status TEXT NOT NULL DEFAULT 'uploaded',
    processing_started_at DATETIME,
    processing_completed_at DATETIME,
    processing_error TEXT,
    docling_document_path TEXT,
    total_pages INTEGER,
    neo4j_document_node_id TEXT,
    source_type TEXT NOT NULL DEFAULT 'upload',
    source_url TEXT
)
"""

DOCUMENT_PAGES_SCHEMA = """
CREATE TABLE IF NOT EXISTS document_pages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_id INTEGER NOT NULL,
    page_number INTEGER NOT NULL,
    content TEXT NOT NULL,
    page_metadata TEXT,
    neo4j_page_node_id TEXT,
    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE,
    UNIQUE(file_id, page_number)
)
"""

INDEX_STATEMENTS = [
    "CREATE INDEX IF NOT EXISTS idx_files_status ON files(status)",
    "CREATE INDEX IF NOT EXISTS idx_files_hash ON files(file_hash)",
    "CREATE INDEX IF NOT EXISTS idx_files_uploaded ON files(uploaded_at)",
    "CREATE INDEX IF NOT EXISTS idx_files_source_type ON files(source_type)",
    "CREATE INDEX IF NOT EXISTS idx_pages_file_id ON document_pages(file_id)",
    "CREATE INDEX IF NOT EXISTS idx_pages_file_page ON document_pages(file_id, page_number)",
]

LEGACY_TABLES = ("documents", "processed_documents")


def backup_database(db_path: Path) -> Path:
    backup_path = db_path.with_suffix(f"{db_path.suffix}.backup_{datetime.now():%Y%m%d_%H%M%S}")
    shutil.copy2(db_path, backup_path)
    return backup_path


def ensure_canonical_schema(db_path: Path, drop_legacy: bool = False) -> bool:
    db_path.parent.mkdir(parents=True, exist_ok=True)

    with sqlite3.connect(db_path) as conn:
        cursor = conn.cursor()
        cursor.execute(FILES_SCHEMA)
        cursor.execute(DOCUMENT_PAGES_SCHEMA)
        for statement in INDEX_STATEMENTS:
            cursor.execute(statement)

        cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
        tables = {row[0] for row in cursor.fetchall()}
        legacy_tables = sorted(set(LEGACY_TABLES) & tables)

        if drop_legacy:
            for table in legacy_tables:
                cursor.execute(f"DROP TABLE IF EXISTS {table}")
            tables -= set(legacy_tables)
            legacy_tables = []

        conn.commit()

    print(f"✅ Canonical tables ensured at: {db_path}")
    print("✅ Supported footprint: files + document_pages")
    if legacy_tables:
        print(f"⚠️ Legacy tables remain present: {legacy_tables}")
        print("   They are no longer used by the Python service.")
    elif drop_legacy:
        print("✅ Legacy compatibility tables were removed.")

    return True


def main() -> None:
    parser = argparse.ArgumentParser(description="Ensure AspireAI uses the canonical Python database footprint.")
    parser.add_argument(
        "--db-path",
        default="database/data-resources.db",
        help="Path to the SQLite database (default: database/data-resources.db)",
    )
    parser.add_argument(
        "--drop-legacy",
        action="store_true",
        help="Drop retired documents / processed_documents tables after backing up the database.",
    )
    args = parser.parse_args()

    db_path = Path(args.db_path)
    backup_path = None
    if args.drop_legacy and db_path.exists():
        backup_path = backup_database(db_path)
        print(f"🗄️ Backup created at: {backup_path}")

    ensure_canonical_schema(db_path, drop_legacy=args.drop_legacy)


if __name__ == "__main__":
    main()
