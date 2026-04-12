from __future__ import annotations

from contextlib import contextmanager
from datetime import UTC, datetime
from typing import Any


FILE_COLUMNS = [
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
    "tenant_id",
    "source_type",
    "source_url",
]

PAGE_COLUMNS = [
    "id",
    "file_id",
    "page_number",
    "content",
    "page_metadata",
    "neo4j_page_node_id",
]

COLUMN_DEFAULTS = {
    "file_hash": "",
    "file_size": 0,
    "status": "uploaded",
    "tenant_id": "default",
    "source_type": "upload",
}


class FakePostgresState:
    def __init__(self):
        self.tables: dict[str, dict[str, Any]] = {}
        self.indexes: set[str] = set()
        self.next_file_id = 1
        self.next_page_id = 1

    def ensure_table(self, table_name: str, columns: list[str]) -> None:
        if table_name in self.tables:
            return
        self.tables[table_name] = {"columns": list(columns), "rows": []}

    def ensure_column(self, table_name: str, column_name: str) -> None:
        self.ensure_table(table_name, [])
        table = self.tables[table_name]
        if column_name in table["columns"]:
            return
        table["columns"].append(column_name)
        default = COLUMN_DEFAULTS.get(column_name)
        for row in table["rows"]:
            row[column_name] = default

    def file_tuple(self, row: dict[str, Any]) -> tuple:
        return tuple(row.get(column) for column in FILE_COLUMNS)

    def page_tuple(self, row: dict[str, Any]) -> tuple:
        return tuple(row.get(column) for column in PAGE_COLUMNS)


class FakeCursor:
    def __init__(self, state: FakePostgresState):
        self.state = state
        self._result: list[tuple] = []

    def execute(self, sql: str, params=None):
        params = params or ()
        normalized = " ".join(sql.split()).lower()

        if "create table if not exists files" in normalized:
            self.state.ensure_table("files", FILE_COLUMNS)
            self._result = []
            return self

        if "create table if not exists document_pages" in normalized:
            self.state.ensure_table("document_pages", PAGE_COLUMNS)
            self._result = []
            return self

        if normalized.startswith("alter table "):
            parts = normalized.split()
            table_name = parts[2]
            column_name = parts[parts.index("exists") + 1] if "exists" in parts else parts[5]
            self.state.ensure_column(table_name, column_name)
            self._result = []
            return self

        if normalized.startswith("create index") or normalized.startswith("create unique index"):
            parts = normalized.split()
            index_name = parts[parts.index("on") - 1]
            self.state.indexes.add(index_name)
            self._result = []
            return self

        if "from information_schema.tables" in normalized:
            self._result = [(name,) for name in sorted(self.state.tables)]
            return self

        if "from information_schema.columns" in normalized:
            table_name = params[0] if params else "files"
            table = self.state.tables.get(table_name, {"columns": []})
            self._result = [(name,) for name in table["columns"]]
            return self

        if normalized == "select 1":
            self._result = [(1,)]
            return self

        if normalized.startswith("insert into files"):
            row = {
                "id": self.state.next_file_id,
                "file_name": params[0],
                "original_file_name": params[1],
                "file_path": params[2],
                "file_hash": params[3],
                "file_size": params[4],
                "mime_type": params[5],
                "uploaded_at": params[6],
                "status": params[7],
                "tenant_id": params[8],
                "processing_started_at": None,
                "processing_completed_at": None,
                "processing_error": None,
                "docling_document_path": None,
                "total_pages": None,
                "neo4j_document_node_id": None,
                "source_type": params[9],
                "source_url": params[10],
            }
            self.state.tables["files"]["rows"].append(row)
            self.state.next_file_id += 1
            self._result = [(row["id"],)]
            return self

        if "from files" in normalized and "where id = %s" in normalized:
            file_id = params[0]
            rows = [
                self.state.file_tuple(row)
                for row in self.state.tables["files"]["rows"]
                if row["id"] == file_id
            ]
            self._result = rows[:1]
            return self

        if "from files" in normalized and "where lower(status) in ('uploaded', 'error')" in normalized:
            rows = [
                row
                for row in self.state.tables["files"]["rows"]
                if row["status"].lower() in {"uploaded", "error"}
            ]
            rows.sort(key=lambda row: row["uploaded_at"])
            self._result = [self.state.file_tuple(row) for row in rows]
            return self

        if "from files" in normalized and "order by uploaded_at desc" in normalized:
            rows = list(self.state.tables["files"]["rows"])
            rows.sort(key=lambda row: row["uploaded_at"], reverse=True)
            self._result = [self.state.file_tuple(row) for row in rows]
            return self

        if normalized.startswith("update files set docling_document_path"):
            file_id = params[3]
            row = self._get_file(file_id)
            row["docling_document_path"] = params[0]
            row["total_pages"] = params[1]
            row["neo4j_document_node_id"] = params[2]
            self._result = []
            return self

        if normalized.startswith("update files set status = %s where id = %s"):
            file_id = params[1]
            row = self._get_file(file_id)
            row["status"] = params[0]
            self._result = []
            return self

        if normalized.startswith("update files set status = %s, processing_started_at = current_timestamp"):
            file_id = params[1]
            row = self._get_file(file_id)
            row["status"] = params[0]
            row["processing_started_at"] = datetime.now(UTC)
            row["processing_completed_at"] = None
            row["processing_error"] = None
            row["docling_document_path"] = None
            row["total_pages"] = None
            row["neo4j_document_node_id"] = None
            self._result = []
            return self

        if normalized.startswith("update files set status = %s, processing_completed_at = current_timestamp, processing_error = null"):
            file_id = params[1]
            row = self._get_file(file_id)
            row["status"] = params[0]
            row["processing_completed_at"] = datetime.now(UTC)
            row["processing_error"] = None
            self._result = []
            return self

        if normalized.startswith("update files set status = %s, processing_completed_at = current_timestamp, processing_error = %s"):
            file_id = params[2]
            row = self._get_file(file_id)
            row["status"] = params[0]
            row["processing_completed_at"] = datetime.now(UTC)
            row["processing_error"] = params[1]
            self._result = []
            return self

        if normalized.startswith("update files set status = %s, processing_started_at = null"):
            file_id = params[1]
            row = self._get_file(file_id)
            row["status"] = params[0]
            row["processing_started_at"] = None
            row["processing_completed_at"] = None
            row["processing_error"] = None
            row["docling_document_path"] = None
            row["total_pages"] = None
            row["neo4j_document_node_id"] = None
            self._result = []
            return self

        if normalized.startswith("delete from document_pages where file_id = %s"):
            file_id = params[0]
            page_table = self.state.tables["document_pages"]["rows"]
            self.state.tables["document_pages"]["rows"] = [
                row for row in page_table if row["file_id"] != file_id
            ]
            self._result = []
            return self

        if normalized.startswith("insert into document_pages"):
            file_id, page_number, content, page_metadata, neo4j_page_node_id = params
            existing = next(
                (
                    row
                    for row in self.state.tables["document_pages"]["rows"]
                    if row["file_id"] == file_id and row["page_number"] == page_number
                ),
                None,
            )
            if existing is None:
                existing = {
                    "id": self.state.next_page_id,
                    "file_id": file_id,
                    "page_number": page_number,
                    "content": content,
                    "page_metadata": page_metadata,
                    "neo4j_page_node_id": neo4j_page_node_id,
                }
                self.state.tables["document_pages"]["rows"].append(existing)
                self.state.next_page_id += 1
            else:
                existing["content"] = content
                existing["page_metadata"] = page_metadata
                existing["neo4j_page_node_id"] = neo4j_page_node_id
            self._result = [(existing["id"],)]
            return self

        if normalized.startswith("select id, file_id, page_number, content, page_metadata, neo4j_page_node_id from document_pages where file_id = %s and page_number = %s"):
            file_id, page_number = params
            rows = [
                self.state.page_tuple(row)
                for row in self.state.tables["document_pages"]["rows"]
                if row["file_id"] == file_id and row["page_number"] == page_number
            ]
            self._result = rows[:1]
            return self

        if normalized.startswith("select id, file_id, page_number, content, page_metadata, neo4j_page_node_id from document_pages where file_id = %s order by page_number"):
            file_id = params[0]
            rows = [
                row
                for row in self.state.tables["document_pages"]["rows"]
                if row["file_id"] == file_id
            ]
            rows.sort(key=lambda row: row["page_number"])
            self._result = [self.state.page_tuple(row) for row in rows]
            return self

        if normalized.startswith("select count(*) from files"):
            self._result = [(len(self.state.tables.get("files", {"rows": []})["rows"]),)]
            return self

        if normalized.startswith("select count(*) from document_pages where file_id = %s"):
            file_id = params[0]
            count = sum(
                1
                for row in self.state.tables["document_pages"]["rows"]
                if row["file_id"] == file_id
            )
            self._result = [(count,)]
            return self

        if normalized.startswith("select count(*) from document_pages"):
            self._result = [(len(self.state.tables.get("document_pages", {"rows": []})["rows"]),)]
            return self

        raise AssertionError(f"Unhandled SQL in fake Postgres driver: {sql}")

    def fetchone(self):
        return self._result[0] if self._result else None

    def fetchall(self):
        return list(self._result)

    def _get_file(self, file_id: int) -> dict[str, Any]:
        row = next(
            (row for row in self.state.tables["files"]["rows"] if row["id"] == file_id),
            None,
        )
        if row is None:
            raise AssertionError(f"Missing fake file row {file_id}")
        return row


class FakeConnection:
    def __init__(self, state: FakePostgresState):
        self.state = state

    def cursor(self) -> FakeCursor:
        return FakeCursor(self.state)

    def commit(self) -> None:
        return None

    def rollback(self) -> None:
        return None


class FakeConnectionPool:
    states: dict[str, FakePostgresState] = {}

    def __init__(self, conninfo: str, max_connections: int = 10, timeout: float = 30.0):
        self.conninfo = conninfo
        self.max_connections = max_connections
        self.timeout = timeout
        self.state = self.states.setdefault(conninfo, FakePostgresState())

    @contextmanager
    def get_connection(self):
        yield FakeConnection(self.state)

    def close_all(self):
        return None

    def get_statistics(self) -> dict[str, Any]:
        return {
            "driver": "fake-postgres",
            "max_connections": self.max_connections,
            "timeout": self.timeout,
        }

    @classmethod
    def reset(cls):
        cls.states = {}
