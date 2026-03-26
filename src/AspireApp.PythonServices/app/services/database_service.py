import sqlite3
import json
import os
import time
import logging
import stat
import threading
from datetime import UTC, datetime
from typing import List, Optional, Dict, Any, Union
from pathlib import Path, PureWindowsPath
from contextlib import contextmanager
import queue
import weakref
from uuid import uuid4

from ..models.models import Document, ProcessingStatus

logger = logging.getLogger(__name__)


class ConnectionPool:
    """Thread-safe SQLite connection pool for better concurrency"""
    
    def __init__(self, db_path: str, max_connections: int = 10, timeout: float = 30.0):
        self.db_path = db_path
        self.max_connections = max_connections
        self.timeout = timeout
        self._pool = queue.Queue(maxsize=max_connections)
        self._lock = threading.Lock()
        self._created_connections = 0
        
    def _create_connection(self) -> sqlite3.Connection:
        """Create a new connection with optimal settings"""
        conn = sqlite3.connect(
            self.db_path, 
            timeout=self.timeout,
            check_same_thread=False  # Allow connection sharing between threads
        )
        
        # Apply optimizations for concurrent access
        conn.execute("PRAGMA journal_mode=WAL")
        conn.execute("PRAGMA synchronous=NORMAL")
        conn.execute("PRAGMA temp_store=memory")
        conn.execute("PRAGMA mmap_size=268435456")  # 256MB
        conn.execute("PRAGMA cache_size=-64000")    # 64MB cache
        conn.execute("PRAGMA busy_timeout=30000")   # 30 second busy timeout
        
        return conn
    
    @contextmanager
    def get_connection(self):
        """Get a connection from the pool"""
        conn = None
        try:
            # Try to get an existing connection
            try:
                conn = self._pool.get_nowait()
            except queue.Empty:
                # Create new connection if pool is empty and under limit
                with self._lock:
                    if self._created_connections < self.max_connections:
                        conn = self._create_connection()
                        self._created_connections += 1
                    else:
                        # Wait for an available connection
                        conn = self._pool.get(timeout=self.timeout)
            
            yield conn
            
        except Exception as e:
            # If connection is bad, don't return it to pool
            if conn:
                try:
                    conn.close()
                except:
                    pass
                with self._lock:
                    self._created_connections -= 1
            raise e
        else:
            # Return healthy connection to pool
            if conn:
                try:
                    # Test connection health before returning
                    conn.execute("SELECT 1")
                    self._pool.put_nowait(conn)
                except (queue.Full, sqlite3.Error):
                    # Pool full or connection bad, close it
                    try:
                        conn.close()
                    except:
                        pass
                    with self._lock:
                        self._created_connections -= 1
    
    def close_all(self):
        """Close all connections in the pool"""
        with self._lock:
            while not self._pool.empty():
                try:
                    conn = self._pool.get_nowait()
                    conn.close()
                except (queue.Empty, sqlite3.Error):
                    pass
            self._created_connections = 0


class DatabaseService:
    """
    Simplified database service for file upload and document processing lifecycle.
    
    Schema Design:
    - files: Single table tracking upload ? processing ? completion
    - document_pages: Page-level content for RAG retrieval
    
    Workflow:
    1. Blazor uploads file ? creates 'files' record (status='uploaded')
    2. Python service detects unprocessed files
    3. Docling processes document ? updates status, creates pages
    4. Future: Pages linked to Neo4j for GraphRAG
    """
    
    # Class-level pool management to ensure singleton behavior
    _pools: Dict[str, ConnectionPool] = {}
    _pools_lock = threading.Lock()
    
    def __init__(self, db_path: str = None):
        # Determine database path with env override and sensible fallbacks
        env_path = os.environ.get("ASPIRE_DB_PATH")
        docs_db = Path(env_path) if env_path else Path("/app/docs-database/data-resources.db")
        volume_db = Path("/app/database/data-resources.db")

        if db_path:
            self.db_path = str(Path(db_path))
            logger.info(f"Using explicit database path: {self.db_path}")
        elif env_path:
            self.db_path = str(docs_db)
            logger.info(f"Using database path from ASPIRE_DB_PATH: {self.db_path}")
        elif docs_db.exists() or docs_db.parent.exists():
            self.db_path = str(docs_db)
            logger.info(f"Using docs-mounted database path: {self.db_path}")
        else:
            self.db_path = str(volume_db)
            logger.info(f"Using volume-backed database path: {self.db_path}")

        self._ensure_database_directory()
        
        # Get or create connection pool for this database path
        with self._pools_lock:
            if self.db_path not in self._pools:
                self._pools[self.db_path] = ConnectionPool(self.db_path)
            self._pool = self._pools[self.db_path]
        
        self._ensure_database_schema()
        self._runtime_data_roots = self._build_runtime_data_roots()
        
        # Statistics tracking
        self._stats = {
            'queries_executed': 0,
            'transactions_committed': 0,
            'retries_performed': 0,
            'lock_timeouts': 0,
            'last_health_check': None
        }
        self._stats_lock = threading.Lock()

    def _ensure_database_directory(self):
        """Ensure the database directory exists with proper permissions"""
        try:
            db_dir = Path(self.db_path).parent
            # Create directory if it doesn't exist
            if not db_dir.exists():
                logger.info(f"Creating database directory: {db_dir}")
                db_dir.mkdir(parents=True, exist_ok=True)
            # Try to set proper permissions if we can
            try:
                if os.access(db_dir, os.W_OK | os.X_OK):
                    os.chmod(db_dir, stat.S_IRWXU | stat.S_IRGRP | stat.S_IXGRP | stat.S_IROTH | stat.S_IXOTH)
                    logger.debug(f"Set permissions on database directory: {db_dir}")
                else:
                    logger.debug(f"Skipping chmod on database directory (insufficient rights): {db_dir}")
            except (OSError, PermissionError) as e:
                logger.info(f"Skipping chmod on database directory (likely bind mount): {e}")
            # Check if we can write to the directory
            test_file = db_dir / f".write_test_{os.getpid()}_{threading.get_ident()}_{uuid4().hex}"
            try:
                with open(test_file, 'w', encoding='utf-8') as f:
                    f.write("test")
                if test_file.exists():
                    test_file.unlink()
                logger.info(f"Database directory is writable: {db_dir}")
            except Exception as e:
                logger.error(f"Database directory is not writable: {db_dir}, error: {e}")
                raise RuntimeError(f"Cannot write to database directory: {db_dir}. Error: {e}")
        except Exception as e:
            logger.error(f"Error ensuring database directory: {e}")
            raise RuntimeError(f"Failed to ensure database directory for {self.db_path}: {e}")

    def _ensure_database_schema(self):
        """
        Ensure the simplified database schema exists.
        
        Schema:
        - files: Single source of truth for file lifecycle (upload ? processing ? completion)
        - document_pages: Page-level content extracted by docling
        """
        try:
            logger.info(f"Using database path: {self.db_path}")
            self._test_database_connection()
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                
                # Create unified files table
                cursor.execute("""
                    CREATE TABLE IF NOT EXISTS files (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        
                        -- Core file identification
                        file_name TEXT NOT NULL,
                        original_file_name TEXT NOT NULL,
                        file_path TEXT NOT NULL,
                        file_hash TEXT NOT NULL DEFAULT '',
                        
                        -- File metadata
                        file_size INTEGER NOT NULL DEFAULT 0,
                        mime_type TEXT,
                        
                        -- Upload tracking
                        uploaded_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        
                        -- Processing lifecycle (uploaded ? processing ? processed | error)
                        status TEXT NOT NULL DEFAULT 'uploaded',
                        processing_started_at DATETIME,
                        processing_completed_at DATETIME,
                        processing_error TEXT,
                        
                        -- Docling processing output
                        docling_document_path TEXT,
                        total_pages INTEGER,
                        
                        -- Neo4j integration (future)
                        neo4j_document_node_id TEXT,
                        
                        -- Future extensibility (website scraping, etc.)
                        source_type TEXT NOT NULL DEFAULT 'upload',
                        source_url TEXT
                    )
                """)
                
                # Create document_pages table for RAG retrieval
                cursor.execute("""
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
                """)
                
                # Create indexes for performance
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_status ON files(status)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_hash ON files(file_hash)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_uploaded ON files(uploaded_at)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_source_type ON files(source_type)")
                
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_pages_file_id ON document_pages(file_id)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_pages_file_page ON document_pages(file_id, page_number)")
                
                conn.commit()
                logger.info(f"? Simplified database schema initialized successfully at: {self.db_path}")
                
                # Log database file info
                db_file = Path(self.db_path)
                if db_file.exists():
                    size = db_file.stat().st_size
                    logger.info(f"Database file size: {size} bytes")
                
        except Exception as e:
            logger.error(f"Failed to initialize database: {e}")
            raise RuntimeError(f"Failed to initialize database at {self.db_path}: {e}")

    def _test_database_connection(self):
        """Test if the database file can be opened/created."""
        try:
            with self._pool.get_connection() as conn:
                conn.execute("SELECT 1")
        except Exception as e:
            raise RuntimeError(f"Database connection test failed: {e}")

    def health_check(self):
        """Simple health check for the database connection."""
        try:
            self._test_database_connection()
            return {"status": "healthy"}
        except Exception as e:
            return {"status": "unhealthy", "error": str(e)}

    # ==================== File Management Methods ====================
    
    def get_file_by_id(self, file_id: int) -> Optional[Dict[str, Any]]:
        """Get file record by ID"""
        try:
            with self._pool.get_connection() as conn:
                row = self._fetch_file_row(conn, file_id)

            if row is None:
                row = self._fetch_file_row_from_fresh_connection(file_id)
                if row is not None:
                    logger.warning(
                        "File %s was not visible through the pooled SQLite connection; "
                        "a fresh connection located the row.",
                        file_id,
                    )

            if row:
                return self._row_to_file_dict(row)

            fallback_file = next(
                (
                    file_dict
                    for file_dict in self.get_all_files()
                    if file_dict.get("id") == file_id
                ),
                None,
            )
            if fallback_file is not None:
                logger.warning(
                    "File %s was not visible through direct SQLite lookup; "
                    "the fallback full-file scan located the row.",
                    file_id,
                )
                return fallback_file

            return None
        except Exception as e:
            logger.error(f"Error fetching file {file_id}: {e}")
            raise

    def get_all_files(self) -> List[Dict[str, Any]]:
        """Return all files from the database"""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    SELECT id, file_name, original_file_name, file_path, file_hash,
                           file_size, mime_type, uploaded_at, status,
                           processing_started_at, processing_completed_at, processing_error,
                           docling_document_path, total_pages, neo4j_document_node_id,
                           source_type, source_url
                    FROM files ORDER BY uploaded_at DESC
                """)
                rows = cursor.fetchall()
                logger.info(f"Fetched {len(rows)} files from database")
                return [self._row_to_file_dict(row) for row in rows]
        except Exception as e:
            logger.error(f"Error fetching all files: {e}")
            raise

    def get_unprocessed_files(self) -> List[Dict[str, Any]]:
        """Get all files currently eligible for processing or retry."""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    SELECT id, file_name, original_file_name, file_path, file_hash,
                           file_size, mime_type, uploaded_at, status,
                           processing_started_at, processing_completed_at, processing_error,
                           docling_document_path, total_pages, neo4j_document_node_id,
                           source_type, source_url
                    FROM files
                    WHERE LOWER(status) IN ('uploaded', 'error')
                    ORDER BY uploaded_at ASC
                """)
                rows = cursor.fetchall()
                logger.info(f"Found {len(rows)} files ready for processing")
                return [self._row_to_file_dict(row) for row in rows]
        except Exception as e:
            logger.error(f"Error fetching unprocessed files: {e}")
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
        source_type: str = "upload",
        source_url: Optional[str] = None,
    ) -> int:
        """Create a file row using the canonical `files` contract."""
        try:
            normalized_status = self._normalize_file_status(status)
            uploaded_at_value = uploaded_at
            if isinstance(uploaded_at_value, datetime):
                uploaded_at_value = uploaded_at_value.isoformat()
            elif uploaded_at_value is None:
                uploaded_at_value = datetime.now(UTC).isoformat()

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
                        source_type,
                        source_url
                    )
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
                        source_type,
                        source_url,
                    ),
                )
                conn.commit()
                file_id = cursor.lastrowid
                logger.debug("Created file record %s with status '%s'", file_id, normalized_status)
                return file_id
        except Exception as e:
            logger.error(f"Error creating file record for {file_name}: {e}")
            raise

    def resolve_upload_path(self, source: Union[Document, Dict[str, Any]]) -> Path:
        """
        Resolve the physical file path for an uploaded document.

        The database stores the upload directory in `file_path` and the timestamped
        filename in `file_name` / `Document.filename`. This helper joins them, then
        adds container guardrails so Windows host paths such as
        `C:\\repo\\AspireAI\\data` can be mapped to the runtime mount at `/app/data`.
        """
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
                resolved = candidate.resolve()
                logger.debug(
                    "Resolved upload path '%s' + '%s' to '%s'.",
                    stored_path,
                    safe_file_name,
                    resolved,
                )
                return resolved

        checked_paths = ", ".join(str(candidate) for candidate in candidates)
        raise FileNotFoundError(
            f"Unable to resolve uploaded file '{safe_file_name}' from stored path '{stored_path}'. "
            f"Checked: {checked_paths}"
        )

    def _build_runtime_data_roots(self) -> List[Path]:
        """Build the set of data roots that may contain uploaded files."""
        roots: List[Path] = []
        env_root = os.environ.get("ASPIRE_DATA_PATH")
        service_file = Path(__file__).resolve()
        if env_root:
            roots.append(Path(env_root))

        roots.append(Path("/app/data"))
        if len(service_file.parents) > 4:
            roots.append(service_file.parents[4] / "data")
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
        """Generate candidate physical paths for an uploaded file."""
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
        """Expand a stored path into direct and mount-mapped runtime candidates."""
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
        """Create a direct `Path` candidate when the stored path matches the runtime OS."""
        if self._looks_like_windows_path(raw_path) and os.name != "nt":
            return None
        return Path(raw_path)

    def _extract_runtime_relative_parts(self, raw_path: str) -> Optional[List[str]]:
        """
        Extract the path relative to the shared data root.

        Examples:
        - `C:\\repo\\AspireAI\\data\\foo.pdf` -> ['foo.pdf']
        - `C:\\repo\\AspireAI\\data\\uploads\\foo.pdf` -> ['uploads', 'foo.pdf']
        - `/app/data/foo.pdf` -> ['foo.pdf']
        - `uploads/foo.pdf` -> ['uploads', 'foo.pdf']
        """
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
        """Split a stored path into stable segments across Windows and POSIX forms."""
        if self._looks_like_windows_path(raw_path):
            return [
                part
                for part in PureWindowsPath(raw_path).parts
                if part not in {"\\", "/"} and not part.endswith(":\\") and not part.endswith(":")
            ]

        return [part for part in Path(raw_path).parts if part not in {"\\", "/"}]

    def _combine_path(self, directory: str, file_name: str) -> str:
        """Combine a stored directory path and file name without assuming the current OS."""
        if directory.endswith(("\\", "/")):
            return f"{directory}{file_name}"

        if "\\" in directory and "/" not in directory:
            return f"{directory}\\{file_name}"

        return f"{directory}/{file_name}"

    def _path_includes_filename(self, raw_path: str, file_name: str) -> bool:
        """Return True when the stored path already ends with the file name."""
        normalized = raw_path.replace("\\", "/").rstrip("/")
        return normalized.lower().endswith(f"/{file_name.lower()}") or normalized.lower() == file_name.lower()

    def _looks_like_windows_path(self, raw_path: str) -> bool:
        """Detect Windows-style stored paths regardless of the current runtime OS."""
        return (len(raw_path) >= 2 and raw_path[1] == ":") or ("\\" in raw_path)

    def _looks_like_absolute_path(self, raw_path: str) -> bool:
        """Detect absolute paths across Windows and POSIX styles."""
        return raw_path.startswith("/") or self._looks_like_windows_path(raw_path)

    def update_file_status(self, file_id: int, status: str, error: str = None) -> None:
        """
        Update the processing status of a file.
        
        Status values: 'uploaded', 'processing', 'processed', 'error'
        """
        try:
            normalized_status = self._normalize_file_status(status)
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                
                if normalized_status == 'processing':
                    cursor.execute("""
                        UPDATE files
                        SET status = ?,
                            processing_started_at = CURRENT_TIMESTAMP,
                            processing_completed_at = NULL,
                            processing_error = NULL,
                            docling_document_path = NULL,
                            total_pages = NULL,
                            neo4j_document_node_id = NULL
                        WHERE id = ?
                    """, (normalized_status, file_id))
                    cursor.execute("DELETE FROM document_pages WHERE file_id = ?", (file_id,))
                elif normalized_status == 'processed':
                    cursor.execute("""
                        UPDATE files 
                        SET status = ?, 
                            processing_completed_at = CURRENT_TIMESTAMP,
                            processing_error = NULL
                        WHERE id = ?
                    """, (normalized_status, file_id))
                elif normalized_status == 'error':
                    cursor.execute("""
                        UPDATE files 
                        SET status = ?, 
                            processing_completed_at = CURRENT_TIMESTAMP,
                            processing_error = ?
                        WHERE id = ?
                    """, (normalized_status, error, file_id))
                elif normalized_status == 'uploaded':
                    cursor.execute("""
                        UPDATE files
                        SET status = ?,
                            processing_started_at = NULL,
                            processing_completed_at = NULL,
                            processing_error = NULL,
                            docling_document_path = NULL,
                            total_pages = NULL,
                            neo4j_document_node_id = NULL
                        WHERE id = ?
                    """, (normalized_status, file_id))
                    cursor.execute("DELETE FROM document_pages WHERE file_id = ?", (file_id,))
                else:
                    cursor.execute("""
                        UPDATE files SET status = ? WHERE id = ?
                    """, (normalized_status, file_id))
                
                conn.commit()
                logger.debug(f"Updated file {file_id} status to '{normalized_status}'")
        except Exception as e:
            logger.error(f"Error updating file {file_id} status: {e}")
            raise

    def update_file_processing_results(self, file_id: int, docling_path: str, 
                                       total_pages: int, neo4j_node_id: str = None) -> None:
        """Update file with docling processing results"""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    UPDATE files 
                    SET docling_document_path = ?,
                        total_pages = ?,
                        neo4j_document_node_id = ?
                    WHERE id = ?
                """, (docling_path, total_pages, neo4j_node_id, file_id))
                conn.commit()
                logger.debug(f"Updated file {file_id} with processing results")
        except Exception as e:
            logger.error(f"Error updating file {file_id} processing results: {e}")
            raise

    # ==================== Document Page Methods ====================

    def save_document_page(self, file_id: int, page_number: int, content: str, 
                          metadata: Dict[str, Any] = None, neo4j_node_id: str = None) -> int:
        """Save a document page"""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    INSERT INTO document_pages 
                    (file_id, page_number, content, page_metadata, neo4j_page_node_id)
                    VALUES (?, ?, ?, ?, ?)
                """, (
                    file_id,
                    page_number,
                    content,
                    json.dumps(metadata) if metadata else None,
                    neo4j_node_id
                ))
                conn.commit()
                page_id = cursor.lastrowid
                logger.debug(f"Saved page {page_number} for file {file_id} (page_id={page_id})")
                return page_id
        except Exception as e:
            logger.error(f"Error saving page {page_number} for file {file_id}: {e}")
            raise

    def get_document_pages(self, file_id: int) -> List[Dict[str, Any]]:
        """Get all pages for a file"""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    SELECT id, file_id, page_number, content, page_metadata, neo4j_page_node_id
                    FROM document_pages
                    WHERE file_id = ?
                    ORDER BY page_number
                """, (file_id,))
                rows = cursor.fetchall()
                
                pages = []
                for row in rows:
                    metadata = json.loads(row[4]) if row[4] else None
                    pages.append({
                        'id': row[0],
                        'file_id': row[1],
                        'page_number': row[2],
                        'content': row[3],
                        'metadata': metadata,
                        'neo4j_page_node_id': row[5]
                    })
                
                return pages
        except Exception as e:
            logger.error(f"Error fetching pages for file {file_id}: {e}")
            raise

    def get_page_by_number(self, file_id: int, page_number: int) -> Optional[Dict[str, Any]]:
        """Get a specific page by file ID and page number"""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    SELECT id, file_id, page_number, content, page_metadata, neo4j_page_node_id
                    FROM document_pages
                    WHERE file_id = ? AND page_number = ?
                """, (file_id, page_number))
                row = cursor.fetchone()
                
                if row:
                    metadata = json.loads(row[4]) if row[4] else None
                    return {
                        'id': row[0],
                        'file_id': row[1],
                        'page_number': row[2],
                        'content': row[3],
                        'metadata': metadata,
                        'neo4j_page_node_id': row[5]
                    }
                return None
        except Exception as e:
            logger.error(f"Error fetching page {page_number} for file {file_id}: {e}")
            raise

    # ==================== Helper Methods ====================

    def _row_to_file_dict(self, row: tuple) -> Dict[str, Any]:
        """Convert database row to file dictionary"""
        return {
            'id': row[0],
            'file_name': row[1],
            'original_file_name': row[2],
            'file_path': row[3],
            'file_hash': row[4],
            'file_size': row[5],
            'mime_type': row[6],
            'uploaded_at': row[7],
            'status': row[8],
            'processing_started_at': row[9],
            'processing_completed_at': row[10],
            'processing_error': row[11],
            'docling_document_path': row[12],
            'total_pages': row[13],
            'neo4j_document_node_id': row[14],
            'source_type': row[15],
            'source_url': row[16]
        }

    def _fetch_file_row(self, conn: sqlite3.Connection, file_id: int):
        cursor = conn.cursor()
        cursor.execute("""
            SELECT id, file_name, original_file_name, file_path, file_hash,
                   file_size, mime_type, uploaded_at, status,
                   processing_started_at, processing_completed_at, processing_error,
                   docling_document_path, total_pages, neo4j_document_node_id,
                   source_type, source_url
            FROM files WHERE id = ?
        """, (file_id,))
        return cursor.fetchone()

    def _fetch_file_row_from_fresh_connection(self, file_id: int):
        conn = sqlite3.connect(
            self.db_path,
            timeout=self._pool.timeout,
            check_same_thread=False,
        )
        try:
            conn.execute("PRAGMA busy_timeout=30000")
            return self._fetch_file_row(conn, file_id)
        finally:
            conn.close()

    def _file_dict_to_document(self, file_dict: Dict[str, Any]) -> Document:
        """Project a canonical `files` row into the document API response model."""
        status = self._normalize_file_status(file_dict.get('status', 'uploaded'))
        return Document(
            id=file_dict['id'],
            filename=file_dict['file_name'],
            original_filename=file_dict['original_file_name'],
            file_path=file_dict['file_path'],
            file_size=file_dict['file_size'],
            mime_type=file_dict['mime_type'],
            upload_date=file_dict['uploaded_at'],
            processed=(status == 'processed'),
            processing_status=status
        )

    def _normalize_file_status(self, status: str) -> str:
        """Normalize incoming statuses to the canonical file lifecycle."""
        normalized = (status or "uploaded").lower()
        status_map = {
            'pending': 'uploaded',
            'processing': 'processing',
            'completed': 'processed',
            'processed': 'processed',
            'error': 'error',
            'failed': 'error',
            'uploaded': 'uploaded',
        }
        return status_map.get(normalized, normalized)

    def list_documents(self) -> List[Document]:
        """Return API document models projected from canonical `files` rows."""
        try:
            return [self._file_dict_to_document(file_dict) for file_dict in self.get_all_files()]
        except Exception as e:
            logger.error(f"Error in list_documents: {e}")
            raise

    def get_document_by_id(self, document_id: int) -> Optional[Document]:
        """Return a single API document model projected from the `files` table."""
        try:
            file_dict = self.get_file_by_id(document_id)
            if file_dict is None:
                return None
            return self._file_dict_to_document(file_dict)
        except Exception as e:
            logger.error(f"Error in get_document_by_id({document_id}): {e}")
            raise

    def list_unprocessed_documents(self) -> List[Document]:
        """Return document models for files still eligible for processing."""
        try:
            return [self._file_dict_to_document(file_dict) for file_dict in self.get_unprocessed_files()]
        except Exception as e:
            logger.error(f"Error in list_unprocessed_documents: {e}")
            raise

    def get_processing_status(self, document_id: int) -> Optional[ProcessingStatus]:
        """Return processing status directly from the canonical `files` row."""
        try:
            file_dict = self.get_file_by_id(document_id)
            if file_dict is None:
                return None

            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    "SELECT COUNT(*) FROM document_pages WHERE file_id = ?",
                    (document_id,),
                )
                count_row = cursor.fetchone()

            processed_pages = count_row[0] if count_row is not None else 0

            return ProcessingStatus(
                document_id=document_id,
                status=self._normalize_file_status(file_dict.get('status')),
                total_pages=file_dict.get('total_pages'),
                processed_pages=processed_pages,
                error_message=file_dict.get('processing_error'),
                started_at=file_dict.get('processing_started_at') or file_dict.get('uploaded_at'),
                completed_at=file_dict.get('processing_completed_at'),
            )
        except Exception as e:
            logger.error(f"Error in get_processing_status({document_id}): {e}")
            raise

    def get_statistics(self) -> Dict[str, Any]:
        """Return database and connection pool statistics for monitoring."""
        try:
            with self._stats_lock:
                stats = dict(self._stats)
            stats.update({
                'connection_pool_size': self._pool._created_connections,
                'max_pool_size': self._pool.max_connections,
                'pool_queue_size': self._pool._pool.qsize(),
            })
            return stats
        except Exception as e:
            logger.error(f"Error in get_statistics: {e}")
            raise

    def get_active_services(self) -> List[Dict[str, Any]]:
        """Return a list of services actively using this database."""
        return [{"name": "python-service", "type": "FastAPI", "status": "active"}]
