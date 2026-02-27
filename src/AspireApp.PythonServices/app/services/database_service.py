import sqlite3
import json
import os
import time
import logging
import stat
import threading
from datetime import datetime
from typing import List, Optional, Dict, Any, Union
from pathlib import Path
from contextlib import contextmanager
import queue
import weakref

from ..models.models import Document, ProcessedDocument, DocumentPage

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

        if env_path:
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
            test_file = db_dir / ".write_test"
            try:
                test_file.touch()
                with open(test_file, 'w') as f:
                    f.write("test")
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
                cursor = conn.cursor()
                cursor.execute("""
                    SELECT id, file_name, original_file_name, file_path, file_hash,
                           file_size, mime_type, uploaded_at, status,
                           processing_started_at, processing_completed_at, processing_error,
                           docling_document_path, total_pages, neo4j_document_node_id,
                           source_type, source_url
                    FROM files WHERE id = ?
                """, (file_id,))
                row = cursor.fetchone()
                
                if row:
                    return self._row_to_file_dict(row)
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
        """Get all files that need processing (status='uploaded')"""
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
                    WHERE status = 'uploaded'
                    ORDER BY uploaded_at ASC
                """)
                rows = cursor.fetchall()
                logger.info(f"Found {len(rows)} unprocessed files")
                return [self._row_to_file_dict(row) for row in rows]
        except Exception as e:
            logger.error(f"Error fetching unprocessed files: {e}")
            raise

    def update_file_status(self, file_id: int, status: str, error: str = None) -> None:
        """
        Update the processing status of a file.
        
        Status values: 'uploaded', 'processing', 'processed', 'error'
        """
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                
                if status == 'processing':
                    cursor.execute("""
                        UPDATE files 
                        SET status = ?, 
                            processing_started_at = CURRENT_TIMESTAMP
                        WHERE id = ?
                    """, (status, file_id))
                elif status == 'processed':
                    cursor.execute("""
                        UPDATE files 
                        SET status = ?, 
                            processing_completed_at = CURRENT_TIMESTAMP,
                            processing_error = NULL
                        WHERE id = ?
                    """, (status, file_id))
                elif status == 'error':
                    cursor.execute("""
                        UPDATE files 
                        SET status = ?, 
                            processing_completed_at = CURRENT_TIMESTAMP,
                            processing_error = ?
                        WHERE id = ?
                    """, (status, error, file_id))
                else:
                    cursor.execute("""
                        UPDATE files SET status = ? WHERE id = ?
                    """, (status, file_id))
                
                conn.commit()
                logger.debug(f"Updated file {file_id} status to '{status}'")
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

    # ==================== Backward Compatibility (Legacy Methods) ====================
    # These methods maintain compatibility with existing code expecting Document model
    
    def get_all_documents(self) -> List[Document]:
        """
        Legacy compatibility method - converts files to Document objects.
        For new code, use get_all_files() instead.
        """
        try:
            files = self.get_all_files()
            return [self._file_dict_to_document(f) for f in files]
        except Exception as e:
            logger.error(f"Error in get_all_documents: {e}")
            raise

    def save_document(self, document: Document) -> int:
        """
        Legacy compatibility method - saves Document as file record.
        For new code, use direct file operations instead.
        """
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    INSERT INTO files 
                    (file_name, original_file_name, file_path, file_size, mime_type, 
                     uploaded_at, status)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                """, (
                    document.filename,
                    document.original_filename,
                    document.file_path,
                    document.file_size,
                    document.mime_type,
                    document.upload_date,
                    self._document_status_to_file_status(document.processing_status)
                ))
                conn.commit()
                file_id = cursor.lastrowid
                logger.debug(f"Saved document as file with ID {file_id}")
                return file_id
        except Exception as e:
            logger.error(f"Error saving document: {e}")
            raise

    def update_processing_status(self, doc_id: int, status: str) -> None:
        """Legacy compatibility method - updates file status"""
        file_status = self._document_status_to_file_status(status)
        self.update_file_status(doc_id, file_status)

    def _file_dict_to_document(self, file_dict: Dict[str, Any]) -> Document:
        """Convert file dictionary to Document model for backward compatibility"""
        return Document(
            id=file_dict['id'],
            filename=file_dict['file_name'],
            original_filename=file_dict['original_file_name'],
            file_path=file_dict['file_path'],
            file_size=file_dict['file_size'],
            mime_type=file_dict['mime_type'],
            upload_date=file_dict['uploaded_at'],
            processed=(file_dict['status'] == 'processed'),
            processing_status=self._file_status_to_document_status(file_dict['status'])
        )

    def _document_status_to_file_status(self, doc_status: str) -> str:
        """Convert legacy Document status to file status"""
        status_map = {
            'pending': 'uploaded',
            'processing': 'processing',
            'completed': 'processed',
            'error': 'error',
            'failed': 'error'
        }
        return status_map.get(doc_status.lower(), 'uploaded')

    def _file_status_to_document_status(self, file_status: str) -> str:
        """Convert file status to legacy Document status"""
        status_map = {
            'uploaded': 'pending',
            'processing': 'processing',
            'processed': 'completed',
            'error': 'error'
        }
        return status_map.get(file_status.lower(), 'pending')

    def get_document(self, document_id: int) -> Optional[Document]:
        """Legacy compatibility: get a single Document by ID.
        For new code, use get_file_by_id() instead."""
        try:
            result = self.get_file_by_id(document_id)
            if result is None:
                return None
            return self._file_dict_to_document(result)
        except Exception as e:
            logger.error(f"Error in get_document({document_id}): {e}")
            raise

    def get_unprocessed_documents(self) -> List[Document]:
        """Legacy compatibility: get unprocessed documents as Document objects.
        For new code, use get_unprocessed_files() instead."""
        try:
            files = self.get_unprocessed_files()
            return [self._file_dict_to_document(f) for f in files]
        except Exception as e:
            logger.error(f"Error in get_unprocessed_documents: {e}")
            raise

    def get_documents_by_status(self, status: str) -> List[Document]:
        """Legacy compatibility: get documents filtered by document-style status.
        Translates the document status to file status before querying."""
        try:
            file_status = self._document_status_to_file_status(status)
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    SELECT id, file_name, original_file_name, file_path, file_hash,
                           file_size, mime_type, uploaded_at, status,
                           processing_started_at, processing_completed_at, processing_error,
                           docling_document_path, total_pages, neo4j_document_node_id,
                           source_type, source_url
                    FROM files
                    WHERE status = ?
                    ORDER BY uploaded_at DESC
                """, (file_status,))
                rows = cursor.fetchall()
                return [self._file_dict_to_document(self._row_to_file_dict(row)) for row in rows]
        except Exception as e:
            logger.error(f"Error in get_documents_by_status('{status}'): {e}")
            raise

    def save_processed_document(self, processed_doc: ProcessedDocument) -> int:
        """Legacy compatibility: persist processing results for a document.
        Updates the existing file record with docling output and marks it processed."""
        try:
            self.update_file_processing_results(
                file_id=processed_doc.document_id,
                docling_path=processed_doc.docling_document_path,
                total_pages=processed_doc.total_pages,
                neo4j_node_id=processed_doc.neo4j_node_id
            )
            self.update_file_status(processed_doc.document_id, 'processed')
            logger.debug(f"Saved processed document for file {processed_doc.document_id}")
            return processed_doc.document_id
        except Exception as e:
            logger.error(f"Error in save_processed_document({processed_doc.document_id}): {e}")
            raise

    def get_processed_document(self, document_id: int) -> Optional[ProcessedDocument]:
        """Legacy compatibility: retrieve ProcessedDocument for a file.
        Returns None if the file doesn't exist or hasn't been processed."""
        try:
            file_dict = self.get_file_by_id(document_id)
            if file_dict is None or not file_dict.get('docling_document_path'):
                return None
            return ProcessedDocument(
                id=file_dict['id'],
                document_id=file_dict['id'],
                docling_document_path=file_dict['docling_document_path'],
                total_pages=file_dict.get('total_pages') or 0,
                processing_date=file_dict.get('processing_completed_at') or datetime.now(),
                neo4j_node_id=file_dict.get('neo4j_document_node_id')
            )
        except Exception as e:
            logger.error(f"Error in get_processed_document({document_id}): {e}")
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

    def get_file_document_sync_status(self) -> Dict[str, Any]:
        """Return sync status between files and documents.
        Schema is now unified (files table only), so this always reports healthy."""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("SELECT COUNT(*) FROM files")
                count = cursor.fetchone()[0]
            return {
                "files_count": count,
                "documents_count": count,
                "sync_health": "healthy",
                "unsynced_files": 0,
                "unsynced_documents": 0
            }
        except Exception as e:
            logger.error(f"Error in get_file_document_sync_status: {e}")
            raise

    def force_sync_files_and_documents(self) -> Dict[str, Any]:
        """No-op sync since schema is now unified (files table only)."""
        return {
            "sync_performed": True,
            "message": "Schema already unified - no sync needed",
            "files_synced": 0
        }