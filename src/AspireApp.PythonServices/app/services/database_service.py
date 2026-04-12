from __future__ import annotations

import json
import logging
import os
import threading
from contextlib import contextmanager
from datetime import UTC, datetime
from pathlib import Path, PureWindowsPath
from typing import Any, Callable, Dict, List, Optional, Union

try:
    from psycopg_pool import ConnectionPool as PsycopgConnectionPool
except ModuleNotFoundError as exc:
    PsycopgConnectionPool = None
    _PSYCOPG_POOL_IMPORT_ERROR = exc
else:
    _PSYCOPG_POOL_IMPORT_ERROR = None

from ..models.models import Document, ProcessingStatus

logger = logging.getLogger(__name__)


class ConnectionPool:
    """Thread-safe PostgreSQL connection pool."""

    def __init__(
        self,
        conninfo: str,
        max_connections: int = 10,
        timeout: float = 30.0,
    ):
        if PsycopgConnectionPool is None:
            raise RuntimeError(
                "psycopg_pool is required to initialize DatabaseService. "
                "Install src\\AspireApp.PythonServices\\requirements.txt before using the live PostgreSQL store."
            ) from _PSYCOPG_POOL_IMPORT_ERROR

        self.conninfo = conninfo
        self.max_connections = max_connections
        self.timeout = timeout
        self._pool = PsycopgConnectionPool(
            conninfo=conninfo,
            min_size=1,
            max_size=max_connections,
            timeout=timeout,
            open=True,
            kwargs={"autocommit": False},
        )
        self._pool.wait()

    @contextmanager
    def get_connection(self):
        with self._pool.connection() as conn:
            try:
                yield conn
            except Exception:
                conn.rollback()
                raise
            else:
                conn.commit()

    def close_all(self):
        self._pool.close()

    def get_statistics(self) -> Dict[str, Any]:
        return {
            "driver": "psycopg_pool",
            "max_connections": self.max_connections,
            "timeout": self.timeout,
        }


class DatabaseService:
    """
    PostgreSQL-backed operational document store.

    Workflow:
    1. Web uploads create `files` rows with status `uploaded`
    2. Python reads unprocessed rows from `files`
    3. Processing writes page content to `document_pages`
    4. Status and processing metadata stay on the `files` row
    """

    _pools: Dict[str, ConnectionPool] = {}
    _pools_lock = threading.Lock()
    _files_column_definitions: Dict[str, str] = {
        "original_file_name": "TEXT NOT NULL DEFAULT ''",
        "file_hash": "TEXT NOT NULL DEFAULT ''",
        "file_size": "BIGINT NOT NULL DEFAULT 0",
        "mime_type": "TEXT",
        "uploaded_at": "TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP",
        "status": "TEXT NOT NULL DEFAULT 'uploaded'",
        "processing_started_at": "TIMESTAMPTZ",
        "processing_completed_at": "TIMESTAMPTZ",
        "processing_error": "TEXT",
        "docling_document_path": "TEXT",
        "total_pages": "INTEGER",
        "neo4j_document_node_id": "TEXT",
        "tenant_id": "TEXT NOT NULL DEFAULT 'default'",
        "source_type": "TEXT NOT NULL DEFAULT 'upload'",
        "source_url": "TEXT",
    }
    _document_pages_column_definitions: Dict[str, str] = {
        "page_metadata": "TEXT",
        "neo4j_page_node_id": "TEXT",
    }

    def __init__(self, db_path: str = None):
        conninfo, source, target = self._resolve_connection_settings(db_path)
        self.connection_string = conninfo
        self.db_path_source = source
        self.db_path = target
        self._pool = None
        self._ensure_connection_pool()
        self._ensure_database_schema()
        self._runtime_data_roots = self._build_runtime_data_roots()

        self._stats = {
            "queries_executed": 0,
            "transactions_committed": 0,
            "retries_performed": 0,
            "lock_timeouts": 0,
            "last_health_check": None,
        }
        self._stats_lock = threading.Lock()

    def _resolve_connection_settings(self, explicit_conninfo: Optional[str]) -> tuple[str, str, str]:
        if explicit_conninfo:
            return explicit_conninfo, "explicit", self._describe_conninfo(explicit_conninfo)

        for env_name in (
            "ASPIRE_DB_CONNECTION_STRING",
            "POSTGRES_CONNECTION_STRING",
            "DATABASE_URL",
        ):
            env_value = os.environ.get(env_name)
            if env_value:
                return env_value, env_name, self._describe_conninfo(env_value)

        conninfo, target = self._build_conninfo_from_environment()
        return conninfo, "environment", target

    def _build_conninfo_from_environment(self) -> tuple[str, str]:
        host = os.environ.get("POSTGRES_HOST") or os.environ.get("PGHOST") or "postgres"
        port = os.environ.get("POSTGRES_PORT") or os.environ.get("PGPORT") or "5432"
        database = (
            os.environ.get("POSTGRES_DATABASE")
            or os.environ.get("POSTGRES_DB")
            or os.environ.get("PGDATABASE")
            or "appdb"
        )
        user = os.environ.get("POSTGRES_USER") or os.environ.get("PGUSER") or "postgres"
        password = os.environ.get("POSTGRES_PASSWORD") or os.environ.get("PGPASSWORD") or ""

        conninfo = (
            f"host={host} port={port} dbname={database} "
            f"user={user} password={password}"
        )
        target = f"postgresql://{host}:{port}/{database}"
        return conninfo, target

    def _describe_conninfo(self, conninfo: str) -> str:
        if "://" in conninfo:
            without_scheme = conninfo.split("://", 1)[1]
            if "@" in without_scheme:
                without_credentials = without_scheme.split("@", 1)[1]
            else:
                without_credentials = without_scheme
            return f"postgresql://{without_credentials}"

        parts: Dict[str, str] = {}
        for token in conninfo.split():
            if "=" not in token:
                continue
            key, value = token.split("=", 1)
            parts[key.strip()] = value.strip()

        host = parts.get("host", "postgres")
        port = parts.get("port", "5432")
        database = parts.get("dbname", parts.get("database", "appdb"))
        return f"postgresql://{host}:{port}/{database}"

    def _ensure_connection_pool(self) -> None:
        with self._pools_lock:
            existing_pool = self._pools.get(self.connection_string)
            if existing_pool is None:
                self._pools[self.connection_string] = ConnectionPool(self.connection_string)
            self._pool = self._pools[self.connection_string]

    def _reset_connection_pool(self, conninfo: str) -> None:
        with self._pools_lock:
            pool = self._pools.pop(conninfo, None)
        if pool is not None:
            pool.close_all()

    def _get_repository_root(self) -> Optional[Path]:
        service_file = Path(__file__).resolve()
        if len(service_file.parents) > 4:
            return service_file.parents[4]
        return None

    def _is_running_in_container(self) -> bool:
        if os.name == "nt":
            return False

        container_flags = (
            os.environ.get("DOTNET_RUNNING_IN_CONTAINER"),
            os.environ.get("RUNNING_IN_CONTAINER"),
            os.environ.get("ASPIRE_RUNNING_IN_CONTAINER"),
        )
        if any(flag and flag.lower() == "true" for flag in container_flags):
            return True

        return Path("/.dockerenv").exists() or Path("/run/.containerenv").exists()

    def _should_prefer_fresh_reads(self) -> bool:
        return False

    @contextmanager
    def _get_fresh_connection(self):
        with self._pool.get_connection() as conn:
            yield conn

    def _ensure_database_schema(self):
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    CREATE TABLE IF NOT EXISTS files (
                        id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        file_name TEXT NOT NULL,
                        original_file_name TEXT NOT NULL,
                        file_path TEXT NOT NULL,
                        file_hash TEXT NOT NULL DEFAULT '',
                        file_size BIGINT NOT NULL DEFAULT 0,
                        mime_type TEXT,
                        uploaded_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        status TEXT NOT NULL DEFAULT 'uploaded',
                        processing_started_at TIMESTAMPTZ,
                        processing_completed_at TIMESTAMPTZ,
                        processing_error TEXT,
                        docling_document_path TEXT,
                        total_pages INTEGER,
                        neo4j_document_node_id TEXT,
                        tenant_id TEXT NOT NULL DEFAULT 'default',
                        source_type TEXT NOT NULL DEFAULT 'upload',
                        source_url TEXT
                    )
                    """
                )
                cursor.execute(
                    """
                    CREATE TABLE IF NOT EXISTS document_pages (
                        id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        file_id INTEGER NOT NULL,
                        page_number INTEGER NOT NULL,
                        content TEXT NOT NULL,
                        page_metadata TEXT,
                        neo4j_page_node_id TEXT,
                        CONSTRAINT fk_document_pages_file
                            FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE
                    )
                    """
                )

                self._ensure_required_columns(cursor, "files", self._files_column_definitions)
                self._ensure_required_columns(cursor, "document_pages", self._document_pages_column_definitions)

                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_status ON files(status)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_hash ON files(file_hash)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_uploaded ON files(uploaded_at)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_tenant ON files(tenant_id)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_tenant_status ON files(tenant_id, status)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_pages_file_id ON document_pages(file_id)")
                cursor.execute(
                    """
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_pages_document_page
                    ON document_pages(file_id, page_number)
                    """
                )

                logger.info("PostgreSQL operational schema initialized at %s", self.db_path)
        except Exception as exc:
            message = self._format_initialization_failure(exc)
            logger.error(message, exc_info=True)
            raise RuntimeError(message) from exc

    def _format_initialization_failure(self, error: Exception) -> str:
        diagnostic = self._collect_schema_diagnostics()
        message = f"Failed to initialize database at {self.db_path}: {type(error).__name__}: {error}"
        if diagnostic:
            message = f"{message}. {diagnostic}"
        return message

    def _collect_schema_diagnostics(self) -> str:
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    SELECT table_name
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                    ORDER BY table_name
                    """
                )
                tables = [row[0] for row in cursor.fetchall()]
                tables_display = ", ".join(tables) if tables else "<none>"

                cursor.execute(
                    """
                    SELECT column_name
                    FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'files'
                    ORDER BY ordinal_position
                    """
                )
                columns = [row[0] for row in cursor.fetchall()]
                if not columns:
                    return f"Existing tables: {tables_display}. Canonical 'files' table is missing."

                columns_display = ", ".join(columns)
                required_columns = {
                    "id",
                    "file_name",
                    "file_path",
                    *self._files_column_definitions.keys(),
                }
                missing_columns = [
                    column_name
                    for column_name in sorted(required_columns)
                    if column_name not in columns
                ]
                if missing_columns:
                    return (
                        f"Existing tables: {tables_display}. "
                        f"Table 'files' columns: {columns_display}. "
                        f"Missing canonical columns: {', '.join(missing_columns)}."
                    )

                return f"Existing tables: {tables_display}. Table 'files' columns: {columns_display}."
        except Exception as diagnostic_error:
            return (
                "Schema diagnostics unavailable: "
                f"{type(diagnostic_error).__name__}: {diagnostic_error}"
            )

    def _ensure_required_columns(
        self,
        cursor,
        table_name: str,
        required_columns: Dict[str, str],
    ) -> None:
        existing_columns = self._get_table_columns(cursor, table_name)
        for column_name, column_definition in required_columns.items():
            if column_name in existing_columns:
                continue

            logger.warning(
                "Database table '%s' is missing column '%s'; applying compatibility migration.",
                table_name,
                column_name,
            )
            cursor.execute(
                f"ALTER TABLE {table_name} ADD COLUMN IF NOT EXISTS {column_name} {column_definition}"
            )
            existing_columns.add(column_name)

    def _get_table_columns(self, cursor, table_name: str) -> set[str]:
        cursor.execute(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = %s
            """,
            (table_name,),
        )
        return {row[0] for row in cursor.fetchall()}

    def _test_database_connection(self):
        try:
            with self._pool.get_connection() as conn:
                conn.cursor().execute("SELECT 1")
        except Exception as exc:
            raise RuntimeError(f"Database connection test failed: {exc}") from exc

    def get_schema_snapshot(self) -> Dict[str, Any]:
        with self._pool.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                ORDER BY table_name
                """
            )
            tables = [row[0] for row in cursor.fetchall()]

            schema: Dict[str, Any] = {
                "database_target": self.db_path,
                "tables": tables,
                "columns": {},
            }
            for table_name in ("files", "document_pages"):
                cursor.execute(
                    """
                    SELECT column_name
                    FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = %s
                    ORDER BY ordinal_position
                    """,
                    (table_name,),
                )
                schema["columns"][table_name] = [row[0] for row in cursor.fetchall()]

            cursor.execute("SELECT COUNT(*) FROM files")
            schema["files_count"] = cursor.fetchone()[0]
            cursor.execute("SELECT COUNT(*) FROM document_pages")
            schema["document_pages_count"] = cursor.fetchone()[0]
            return schema

    def health_check(self):
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("SELECT COUNT(*) FROM files")
                document_count = cursor.fetchone()[0]
                cursor.execute("SELECT COUNT(*) FROM document_pages")
                page_count = cursor.fetchone()[0]

            return {
                "status": "healthy",
                "database_provider": "postgres",
                "database_target": self.db_path,
                "connection_source": self.db_path_source,
                "document_count": document_count,
                "page_count": page_count,
            }
        except Exception as exc:
            return {
                "status": "unhealthy",
                "database_provider": "postgres",
                "database_target": self.db_path,
                "connection_source": self.db_path_source,
                "error": str(exc),
            }

    def get_file_by_id(self, file_id: int) -> Optional[Dict[str, Any]]:
        try:
            with self._pool.get_connection() as conn:
                row = self._fetch_file_row(conn, file_id)
            return self._row_to_file_dict(row) if row else None
        except Exception as exc:
            logger.error("Error fetching file %s: %s", file_id, exc)
            raise

    def get_all_files(self) -> List[Dict[str, Any]]:
        try:
            with self._pool.get_connection() as conn:
                rows = self._fetch_all_file_rows(conn)
            return [self._row_to_file_dict(row) for row in rows]
        except Exception as exc:
            logger.error("Error fetching all files: %s", exc)
            raise

    def get_unprocessed_files(self) -> List[Dict[str, Any]]:
        try:
            with self._pool.get_connection() as conn:
                rows = self._fetch_unprocessed_file_rows(conn)
            return [self._row_to_file_dict(row) for row in rows]
        except Exception as exc:
            logger.error("Error fetching unprocessed files: %s", exc)
            raise

    def create_file_record(
        self,
        *,
        file_name: str,
        original_file_name: str,
        file_path: str,
        file_size: int = 0,
        mime_type: Optional[str] = None,
        file_hash: str = "",
        uploaded_at: Optional[datetime] = None,
        status: str = "uploaded",
        tenant_id: str = "default",
        source_type: str = "upload",
        source_url: Optional[str] = None,
    ) -> int:
        try:
            normalized_status = self._normalize_file_status(status)
            uploaded_at_value = uploaded_at or datetime.now(UTC)

            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    INSERT INTO files (
                        file_name,
                        original_file_name,
                        file_path,
                        file_hash,
                        file_size,
                        mime_type,
                        uploaded_at,
                        status,
                        tenant_id,
                        source_type,
                        source_url
                    )
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                    RETURNING id
                    """,
                    (
                        file_name,
                        original_file_name,
                        file_path,
                        file_hash,
                        file_size,
                        mime_type,
                        uploaded_at_value,
                        normalized_status,
                        tenant_id,
                        source_type,
                        source_url,
                    ),
                )
                file_id = cursor.fetchone()[0]
                logger.debug("Created file record %s with status '%s'", file_id, normalized_status)
                return file_id
        except Exception as exc:
            logger.error("Error creating file record for %s: %s", file_name, exc)
            raise

    def resolve_upload_path(self, source: Union[Document, Dict[str, Any]]) -> Path:
        if isinstance(source, Document):
            stored_path = source.file_path
            file_name = source.filename
        else:
            stored_path = source.get("file_path")
            file_name = source.get("file_name")

        if not stored_path:
            raise ValueError("Cannot resolve upload path because file_path is empty.")

        if not file_name:
            raise ValueError("Cannot resolve upload path because file_name is empty.")

        safe_file_name = Path(file_name.replace("\\", "/")).name
        if safe_file_name != file_name:
            logger.warning(
                "Stored file_name '%s' contained directory segments; using basename '%s'.",
                file_name,
                safe_file_name,
            )

        candidates = self._build_upload_path_candidates(stored_path, safe_file_name)
        for candidate in candidates:
            if candidate.exists() and candidate.is_file():
                return candidate.resolve()

        checked_paths = ", ".join(str(candidate) for candidate in candidates)
        raise FileNotFoundError(
            f"Unable to resolve uploaded file '{safe_file_name}' from stored path '{stored_path}'. "
            f"Checked: {checked_paths}"
        )

    def _build_runtime_data_roots(self) -> List[Path]:
        roots: List[Path] = []
        env_root = os.environ.get("ASPIRE_DATA_PATH")
        repo_root = self._get_repository_root()
        if env_root:
            roots.append(Path(env_root))

        roots.append(Path("/app/data"))
        if repo_root is not None:
            roots.append(repo_root / "data")
        roots.append(Path.cwd() / "data")

        unique_roots: List[Path] = []
        seen: set[str] = set()
        for root in roots:
            normalized = str(root)
            if normalized not in seen:
                seen.add(normalized)
                unique_roots.append(root)
        return unique_roots

    def _build_upload_path_candidates(self, stored_path: str, file_name: str) -> List[Path]:
        normalized_path = stored_path.strip()
        raw_candidates = [
            normalized_path
            if self._path_includes_filename(normalized_path, file_name)
            else self._combine_path(normalized_path, file_name)
        ]

        candidates: List[Path] = []
        seen: set[str] = set()
        for raw_candidate in raw_candidates:
            for candidate in self._expand_runtime_candidates(raw_candidate):
                candidate_key = str(candidate)
                if candidate_key not in seen:
                    seen.add(candidate_key)
                    candidates.append(candidate)

        for runtime_root in self._runtime_data_roots:
            fallback_candidate = runtime_root / file_name
            candidate_key = str(fallback_candidate)
            if candidate_key not in seen:
                seen.add(candidate_key)
                candidates.append(fallback_candidate)

        return candidates

    def _expand_runtime_candidates(self, raw_path: str) -> List[Path]:
        candidates: List[Path] = []
        direct_candidate = self._create_direct_path_candidate(raw_path)
        if direct_candidate is not None:
            candidates.append(direct_candidate)

        relative_parts = self._extract_runtime_relative_parts(raw_path)
        if relative_parts is not None:
            for runtime_root in self._runtime_data_roots:
                candidates.append(runtime_root.joinpath(*relative_parts))

        return candidates

    def _create_direct_path_candidate(self, raw_path: str) -> Optional[Path]:
        if self._looks_like_windows_path(raw_path) and os.name != "nt":
            return None
        return Path(raw_path)

    def _extract_runtime_relative_parts(self, raw_path: str) -> Optional[List[str]]:
        parts = self._split_stored_path(raw_path)
        if not parts:
            return None

        lower_parts = [part.lower() for part in parts]
        if "data" in lower_parts:
            data_index = max(index for index, part in enumerate(lower_parts) if part == "data")
            return parts[data_index + 1 :]

        if lower_parts[0] in {"uploads", "processed"}:
            return parts

        if not self._looks_like_absolute_path(raw_path):
            return parts

        return None

    def _split_stored_path(self, raw_path: str) -> List[str]:
        if self._looks_like_windows_path(raw_path):
            return [
                part
                for part in PureWindowsPath(raw_path).parts
                if part not in {"\\", "/"} and not part.endswith(":\\") and not part.endswith(":")
            ]
        return [part for part in Path(raw_path).parts if part not in {"\\", "/"}]

    def _combine_path(self, directory: str, file_name: str) -> str:
        if directory.endswith(("\\", "/")):
            return f"{directory}{file_name}"
        if "\\" in directory and "/" not in directory:
            return f"{directory}\\{file_name}"
        return f"{directory}/{file_name}"

    def _path_includes_filename(self, raw_path: str, file_name: str) -> bool:
        normalized = raw_path.replace("\\", "/").rstrip("/")
        return normalized.lower().endswith(f"/{file_name.lower()}") or normalized.lower() == file_name.lower()

    def _looks_like_windows_path(self, raw_path: str) -> bool:
        return (len(raw_path) >= 2 and raw_path[1] == ":") or ("\\" in raw_path)

    def _looks_like_absolute_path(self, raw_path: str) -> bool:
        return raw_path.startswith("/") or self._looks_like_windows_path(raw_path)

    def update_file_status(self, file_id: int, status: str, error: str = None) -> None:
        try:
            normalized_status = self._normalize_file_status(status)
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                if normalized_status == "processing":
                    cursor.execute(
                        """
                        UPDATE files
                        SET status = %s,
                            processing_started_at = CURRENT_TIMESTAMP,
                            processing_completed_at = NULL,
                            processing_error = NULL,
                            docling_document_path = NULL,
                            total_pages = NULL,
                            neo4j_document_node_id = NULL
                        WHERE id = %s
                        """,
                        (normalized_status, file_id),
                    )
                    cursor.execute("DELETE FROM document_pages WHERE file_id = %s", (file_id,))
                elif normalized_status == "processed":
                    cursor.execute(
                        """
                        UPDATE files
                        SET status = %s,
                            processing_completed_at = CURRENT_TIMESTAMP,
                            processing_error = NULL
                        WHERE id = %s
                        """,
                        (normalized_status, file_id),
                    )
                elif normalized_status == "error":
                    cursor.execute(
                        """
                        UPDATE files
                        SET status = %s,
                            processing_completed_at = CURRENT_TIMESTAMP,
                            processing_error = %s
                        WHERE id = %s
                        """,
                        (normalized_status, error, file_id),
                    )
                elif normalized_status == "uploaded":
                    cursor.execute(
                        """
                        UPDATE files
                        SET status = %s,
                            processing_started_at = NULL,
                            processing_completed_at = NULL,
                            processing_error = NULL,
                            docling_document_path = NULL,
                            total_pages = NULL,
                            neo4j_document_node_id = NULL
                        WHERE id = %s
                        """,
                        (normalized_status, file_id),
                    )
                    cursor.execute("DELETE FROM document_pages WHERE file_id = %s", (file_id,))
                else:
                    cursor.execute("UPDATE files SET status = %s WHERE id = %s", (normalized_status, file_id))
        except Exception as exc:
            logger.error("Error updating file %s status: %s", file_id, exc)
            raise

    def update_file_processing_results(
        self,
        file_id: int,
        docling_path: str,
        total_pages: int,
        neo4j_node_id: str = None,
    ) -> None:
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    UPDATE files
                    SET docling_document_path = %s,
                        total_pages = %s,
                        neo4j_document_node_id = %s
                    WHERE id = %s
                    """,
                    (docling_path, total_pages, neo4j_node_id, file_id),
                )
        except Exception as exc:
            logger.error("Error updating file %s processing results: %s", file_id, exc)
            raise

    def save_document_page(
        self,
        file_id: int,
        page_number: int,
        content: str,
        metadata: Dict[str, Any] = None,
        neo4j_node_id: str = None,
    ) -> int:
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    INSERT INTO document_pages (file_id, page_number, content, page_metadata, neo4j_page_node_id)
                    VALUES (%s, %s, %s, %s, %s)
                    ON CONFLICT (file_id, page_number) DO UPDATE
                    SET content = EXCLUDED.content,
                        page_metadata = EXCLUDED.page_metadata,
                        neo4j_page_node_id = EXCLUDED.neo4j_page_node_id
                    RETURNING id
                    """,
                    (
                        file_id,
                        page_number,
                        content,
                        json.dumps(metadata) if metadata else None,
                        neo4j_node_id,
                    ),
                )
                return cursor.fetchone()[0]
        except Exception as exc:
            logger.error("Error saving page %s for file %s: %s", page_number, file_id, exc)
            raise

    def get_document_pages(self, file_id: int) -> List[Dict[str, Any]]:
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    SELECT id, file_id, page_number, content, page_metadata, neo4j_page_node_id
                    FROM document_pages
                    WHERE file_id = %s
                    ORDER BY page_number
                    """,
                    (file_id,),
                )
                rows = cursor.fetchall()

            return [self._row_to_page_dict(row) for row in rows]
        except Exception as exc:
            logger.error("Error fetching pages for file %s: %s", file_id, exc)
            raise

    def get_page_by_number(self, file_id: int, page_number: int) -> Optional[Dict[str, Any]]:
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    SELECT id, file_id, page_number, content, page_metadata, neo4j_page_node_id
                    FROM document_pages
                    WHERE file_id = %s AND page_number = %s
                    """,
                    (file_id, page_number),
                )
                row = cursor.fetchone()
            return self._row_to_page_dict(row) if row else None
        except Exception as exc:
            logger.error("Error fetching page %s for file %s: %s", page_number, file_id, exc)
            raise

    def _row_to_page_dict(self, row: tuple) -> Dict[str, Any]:
        metadata = json.loads(row[4]) if row[4] else None
        return {
            "id": row[0],
            "file_id": row[1],
            "page_number": row[2],
            "content": row[3],
            "metadata": metadata,
            "neo4j_page_node_id": row[5],
        }

    def _row_to_file_dict(self, row: tuple) -> Dict[str, Any]:
        return {
            "id": row[0],
            "file_name": row[1],
            "original_file_name": row[2],
            "file_path": row[3],
            "file_hash": row[4],
            "file_size": row[5],
            "mime_type": row[6],
            "uploaded_at": row[7],
            "status": row[8],
            "processing_started_at": row[9],
            "processing_completed_at": row[10],
            "processing_error": row[11],
            "docling_document_path": row[12],
            "total_pages": row[13],
            "neo4j_document_node_id": row[14],
            "tenant_id": row[15],
            "source_type": row[16],
            "source_url": row[17],
        }

    def _fetch_file_row(self, conn, file_id: int):
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT id, file_name, original_file_name, file_path, file_hash,
                   file_size, mime_type, uploaded_at, status,
                   processing_started_at, processing_completed_at, processing_error,
                   docling_document_path, total_pages, neo4j_document_node_id,
                   tenant_id, source_type, source_url
            FROM files
            WHERE id = %s
            """,
            (file_id,),
        )
        return cursor.fetchone()

    def _fetch_all_file_rows(self, conn):
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT id, file_name, original_file_name, file_path, file_hash,
                   file_size, mime_type, uploaded_at, status,
                   processing_started_at, processing_completed_at, processing_error,
                   docling_document_path, total_pages, neo4j_document_node_id,
                   tenant_id, source_type, source_url
            FROM files
            ORDER BY uploaded_at DESC
            """
        )
        return cursor.fetchall()

    def _fetch_unprocessed_file_rows(self, conn):
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT id, file_name, original_file_name, file_path, file_hash,
                   file_size, mime_type, uploaded_at, status,
                   processing_started_at, processing_completed_at, processing_error,
                   docling_document_path, total_pages, neo4j_document_node_id,
                   tenant_id, source_type, source_url
            FROM files
            WHERE LOWER(status) IN ('uploaded', 'error')
            ORDER BY uploaded_at ASC
            """
        )
        return cursor.fetchall()

    def _fetch_file_row_from_fresh_connection(self, file_id: int):
        with self._pool.get_connection() as conn:
            return self._fetch_file_row(conn, file_id)

    def _fetch_rows_from_fresh_connection(self, fetcher: Callable[[Any], list[tuple]]) -> list[tuple]:
        with self._pool.get_connection() as conn:
            return fetcher(conn)

    def _file_dict_to_document(self, file_dict: Dict[str, Any]) -> Document:
        status = self._normalize_file_status(file_dict.get("status", "uploaded"))
        return Document(
            id=file_dict["id"],
            filename=file_dict["file_name"],
            original_filename=file_dict["original_file_name"],
            file_path=file_dict["file_path"],
            file_size=file_dict["file_size"],
            mime_type=file_dict["mime_type"],
            upload_date=file_dict["uploaded_at"],
            processed=(status == "processed"),
            processing_status=status,
        )

    def _normalize_file_status(self, status: str) -> str:
        normalized = (status or "uploaded").lower()
        status_map = {
            "pending": "uploaded",
            "processing": "processing",
            "completed": "processed",
            "processed": "processed",
            "error": "error",
            "failed": "error",
            "uploaded": "uploaded",
        }
        return status_map.get(normalized, normalized)

    def list_documents(self) -> List[Document]:
        return [self._file_dict_to_document(file_dict) for file_dict in self.get_all_files()]

    def get_document_by_id(self, document_id: int) -> Optional[Document]:
        file_dict = self.get_file_by_id(document_id)
        if file_dict is None:
            return None
        return self._file_dict_to_document(file_dict)

    def list_unprocessed_documents(self) -> List[Document]:
        return [self._file_dict_to_document(file_dict) for file_dict in self.get_unprocessed_files()]

    def get_processing_status(self, document_id: int) -> Optional[ProcessingStatus]:
        try:
            file_dict = self.get_file_by_id(document_id)
            if file_dict is None:
                return None

            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("SELECT COUNT(*) FROM document_pages WHERE file_id = %s", (document_id,))
                count_row = cursor.fetchone()

            processed_pages = count_row[0] if count_row is not None else 0
            return ProcessingStatus(
                document_id=document_id,
                status=self._normalize_file_status(file_dict.get("status")),
                total_pages=file_dict.get("total_pages"),
                processed_pages=processed_pages,
                error_message=file_dict.get("processing_error"),
                started_at=file_dict.get("processing_started_at") or file_dict.get("uploaded_at"),
                completed_at=file_dict.get("processing_completed_at"),
            )
        except Exception as exc:
            logger.error("Error in get_processing_status(%s): %s", document_id, exc)
            raise

    def get_statistics(self) -> Dict[str, Any]:
        with self._stats_lock:
            stats = dict(self._stats)
        stats.update(self._pool.get_statistics())
        stats.update(
            {
                "database_provider": "postgres",
                "database_target": self.db_path,
                "connection_source": self.db_path_source,
            }
        )
        return stats

    def get_active_services(self) -> List[Dict[str, Any]]:
        return [{"name": "python-service", "type": "FastAPI", "status": "active"}]
