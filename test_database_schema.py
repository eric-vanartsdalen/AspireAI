#!/usr/bin/env python3
"""
Quick database verification script for the canonical Python footprint.
"""

import os
import sqlite3


FILES_COLUMNS = {
    "id",
    "file_name",
    "original_file_name",
    "file_path",
    "file_hash",
    "file_size",
    "mime_type",
    "uploaded_at",
    "status",
    "processing_started_at",
    "processing_completed_at",
    "processing_error",
    "docling_document_path",
    "total_pages",
    "neo4j_document_node_id",
    "source_type",
    "source_url",
}

DOCUMENT_PAGES_COLUMNS = {
    "id",
    "file_id",
    "page_number",
    "content",
    "page_metadata",
    "neo4j_page_node_id",
}


def _get_table_columns(cursor, table_name: str) -> set[str]:
    cursor.execute(f"PRAGMA table_info({table_name})")
    return {row[1] for row in cursor.fetchall()}


def test_database_schema(db_path: str = "database/data-resources.db") -> bool:
    """Verify that the database matches the canonical `files` + `document_pages` contract."""
    if not os.path.exists(db_path):
        print(f"❌ Database file not found: {db_path}")
        return False

    try:
        with sqlite3.connect(db_path) as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
            tables = {row[0] for row in cursor.fetchall()}

            print(f"📋 Found tables in database: {sorted(tables)}")

            required_tables = {"files", "document_pages"}
            missing_tables = sorted(required_tables - tables)
            if missing_tables:
                print(f"❌ Missing required tables: {missing_tables}")
                return False

            legacy_tables = sorted({"documents", "processed_documents"} & tables)
            if legacy_tables:
                print(f"⚠️ Legacy tables still present (not used by Python): {legacy_tables}")

            files_columns = _get_table_columns(cursor, "files")
            page_columns = _get_table_columns(cursor, "document_pages")

            if not FILES_COLUMNS.issubset(files_columns):
                print(f"❌ Files table is missing columns: {sorted(FILES_COLUMNS - files_columns)}")
                return False

            if not DOCUMENT_PAGES_COLUMNS.issubset(page_columns):
                print(
                    f"❌ document_pages table is missing columns: "
                    f"{sorted(DOCUMENT_PAGES_COLUMNS - page_columns)}"
                )
                return False

            cursor.execute(
                """
                INSERT INTO files (
                    file_name,
                    original_file_name,
                    file_path,
                    file_size,
                    mime_type,
                    status,
                    source_type
                )
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    "test.pdf",
                    "test.pdf",
                    "uploads",
                    1024,
                    "application/pdf",
                    "uploaded",
                    "upload",
                ),
            )
            file_id = cursor.lastrowid
            cursor.execute("SELECT file_name, status FROM files WHERE id = ?", (file_id,))
            inserted = cursor.fetchone()
            cursor.execute("DELETE FROM files WHERE id = ?", (file_id,))
            conn.commit()

            print(f"✅ Insert/select/delete succeeded for canonical files row: {inserted}")
            print("✅ Database schema verification successful")
            print("ℹ️ Supported Python footprint: files + document_pages")
            return True

    except Exception as e:
        print(f"❌ Database test failed: {e}")
        return False


def main() -> None:
    print("🧪 Testing database schema for AspireAI Python service")
    print("=" * 70)

    db_paths = [
        "database/data-resources.db",
        "../database/data-resources.db",
        "src/AspireApp.Web/data-resources.db",
    ]

    for db_path in db_paths:
        if os.path.exists(db_path):
            print(f"📍 Found database at: {db_path}")
            if test_database_schema(db_path):
                print("\n✅ Database test passed")
                print("Next steps:")
                print("  1. Start Aspire if it is not already running")
                print("  2. Verify /documents/ and /processing/status/{id}")
                print("  3. Upload a file through the Blazor UI")
                return

            print(f"\n❌ Database test failed for {db_path}")
        else:
            print(f"ℹ️ Database not found at: {db_path}")

    print("\nℹ️ No usable database found. Start the app once, then rerun this check.")


if __name__ == "__main__":
    main()
