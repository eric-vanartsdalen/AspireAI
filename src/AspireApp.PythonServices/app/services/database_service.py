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
    # Class-level pool management to ensure singleton behavior
    _pools: Dict[str, ConnectionPool] = {}
    _pools_lock = threading.Lock()
    
    def __init__(self, db_path: str = None):
        # Always use the fixed database path
        self.db_path = "/app/database/data-resources.db"
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
                os.chmod(db_dir, stat.S_IRWXU | stat.S_IRGRP | stat.S_IXGRP | stat.S_IROTH | stat.S_IXOTH)
                logger.debug(f"Set permissions on database directory: {db_dir}")
            except (OSError, PermissionError) as e:
                logger.warning(f"Could not set directory permissions: {e}")
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
        """Ensure the database schema exists with both Files and documents tables"""
        max_retries = 1
        try:
            logger.info(f"Using database path: {self.db_path}")
            self._test_database_connection()
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                # Create Files table for C# FileMetadata compatibility
                cursor.execute("""
                    CREATE TABLE IF NOT EXISTS Files (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FileName TEXT NOT NULL,
                        Size INTEGER NOT NULL DEFAULT 0,
                        UploadedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        Status TEXT DEFAULT 'Uploaded',
                        FileHash TEXT DEFAULT ''
                    )
                """)
                
                # Create documents table for Python Document compatibility
                cursor.execute("""
                    CREATE TABLE IF NOT EXISTS documents (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        filename TEXT NOT NULL,
                        original_filename TEXT NOT NULL,
                        file_path TEXT NOT NULL,
                        file_size INTEGER,
                        mime_type TEXT,
                        upload_date DATETIME DEFAULT CURRENT_TIMESTAMP,
                        processed BOOLEAN DEFAULT FALSE,
                        processing_status TEXT DEFAULT 'pending'
                    )
                """)
                
                # Create processed_documents table if it doesn't exist
                cursor.execute("""
                    CREATE TABLE IF NOT EXISTS processed_documents (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        document_id INTEGER REFERENCES documents(id),
                        docling_document_path TEXT NOT NULL,
                        total_pages INTEGER,
                        processing_date DATETIME DEFAULT CURRENT_TIMESTAMP,
                        processing_metadata TEXT,
                        neo4j_node_id TEXT
                    )
                """)
                
                # Create document_pages table if it doesn't exist
                cursor.execute("""
                    CREATE TABLE IF NOT EXISTS document_pages (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        processed_document_id INTEGER REFERENCES processed_documents(id),
                        page_number INTEGER NOT NULL,
                        content TEXT NOT NULL,
                        page_metadata TEXT,
                        neo4j_node_id TEXT
                    )
                """)
                
                # Create indexes for better performance
                # Files table indexes
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_filehash ON Files(FileHash)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_uploadedat ON Files(UploadedAt)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_files_status ON Files(Status)")
                
                # Documents table indexes
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_documents_processed ON documents(processed)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_documents_upload_date ON documents(upload_date)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_documents_status ON documents(processing_status)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_documents_filename ON documents(filename)")
                
                # Related tables indexes
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_processed_documents_document_id ON processed_documents(document_id)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_document_pages_processed_doc_id ON document_pages(processed_document_id)")
                cursor.execute("CREATE INDEX IF NOT EXISTS idx_document_pages_page_number ON document_pages(page_number)")
                
                # Create a table for tracking concurrent access metrics if not exists
                cursor.execute("""
                    CREATE TABLE IF NOT EXISTS service_metrics (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        service_name TEXT NOT NULL,
                        last_activity DATETIME DEFAULT CURRENT_TIMESTAMP,
                        operations_count INTEGER DEFAULT 0,
                        active_connections INTEGER DEFAULT 0
                    )
                """)
                
                # Create a bridge/sync table to track relationships between Files and documents
                cursor.execute("""
                    CREATE TABLE IF NOT EXISTS file_document_bridge (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        file_id INTEGER REFERENCES Files(Id),
                        document_id INTEGER REFERENCES documents(id),
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        sync_status TEXT DEFAULT 'synced'
                    )
                """)
                cursor.execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_file_document_bridge_unique ON file_document_bridge(file_id, document_id)")
                
                conn.commit()
                logger.info(f"Database schema initialized successfully at: {self.db_path}")
                
                # Perform initial data sync between Files and documents tables
                self._sync_files_and_documents(conn)
                
                # Log database file info
                db_file = Path(self.db_path)
                if db_file.exists():
                    size = db_file.stat().st_size
                    logger.info(f"Database file size: {size} bytes")
                
                return  # Success, exit retry loop
                
        except Exception as e:
            logger.error(f"Failed to initialize database: {e}")
            raise RuntimeError(f"Failed to initialize database at {self.db_path}: {e}")

    def _sync_files_and_documents(self, conn: sqlite3.Connection):
        """Sync data between Files and documents tables"""
        try:
            cursor = conn.cursor()
            
            # Check if we have data in Files table that's not in documents
            cursor.execute("""
                SELECT f.Id, f.FileName, f.Size, f.UploadedAt, f.Status 
                FROM Files f
                LEFT JOIN file_document_bridge fdb ON f.Id = fdb.file_id
                WHERE fdb.file_id IS NULL
            """)
            unsynced_files = cursor.fetchall()
            
            synced_count = 0
            for file_row in unsynced_files:
                file_id, filename, size, uploaded_at, status = file_row
                
                # Convert FileMetadata status to Document processing status
                processing_status = self._convert_file_status_to_document_status(status)
                
                # Extract original filename (remove unique suffix if present)
                original_filename = self._extract_original_filename(filename)
                
                # Determine MIME type
                mime_type = self._get_mime_type_from_filename(filename)
                
                # Insert into documents table
                cursor.execute("""
                    INSERT INTO documents 
                    (filename, original_filename, file_path, file_size, mime_type, upload_date, processed, processing_status)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """, (
                    filename,
                    original_filename,
                    filename,  # Use filename as file_path for now
                    size,
                    mime_type,
                    uploaded_at,
                    status.lower() == 'processed',
                    processing_status
                ))
                
                document_id = cursor.lastrowid
                
                # Create bridge record
                cursor.execute("""
                    INSERT INTO file_document_bridge (file_id, document_id, sync_status)
                    VALUES (?, ?, 'synced')
                """, (file_id, document_id))
                
                synced_count += 1
            
            # Check if we have data in documents table that's not in Files
            cursor.execute("""
                SELECT d.id, d.filename, d.file_size, d.upload_date, d.processing_status
                FROM documents d
                LEFT JOIN file_document_bridge fdb ON d.id = fdb.document_id
                WHERE fdb.document_id IS NULL
            """)
            unsynced_documents = cursor.fetchall()
            
            for doc_row in unsynced_documents:
                doc_id, filename, file_size, upload_date, processing_status = doc_row
                
                # Convert Document processing status to FileMetadata status
                file_status = self._convert_document_status_to_file_status(processing_status)
                
                # Insert into Files table
                cursor.execute("""
                    INSERT INTO Files (FileName, Size, UploadedAt, Status, FileHash)
                    VALUES (?, ?, ?, ?, ?)
                """, (
                    filename,
                    file_size or 0,
                    upload_date,
                    file_status,
                    ''  # Empty hash for now
                ))
                
                file_id = cursor.lastrowid
                
                # Create bridge record
                cursor.execute("""
                    INSERT INTO file_document_bridge (file_id, document_id, sync_status)
                    VALUES (?, ?, 'synced')
                """, (file_id, doc_id))
                
                synced_count += 1
            
            if synced_count > 0:
                conn.commit()
                logger.info(f"Synced {synced_count} records between Files and documents tables")
            
        except Exception as e:
            logger.warning(f"Could not sync Files and documents tables: {e}")
    
    def _convert_file_status_to_document_status(self, file_status: str) -> str:
        """Convert FileMetadata status to Document processing status"""
        status_map = {
            'uploaded': 'pending',
            'pending': 'pending',
            'processed': 'completed',
            'processing': 'processing',
            'error': 'error',
            'failed': 'error'
        }
        return status_map.get(file_status.lower(), 'pending')
    
    def _convert_document_status_to_file_status(self, doc_status: str) -> str:
        """Convert Document processing status to FileMetadata status"""
        status_map = {
            'pending': 'Uploaded',
            'processing': 'Processing',
            'completed': 'Processed',
            'error': 'Error',
            'failed': 'Error'
        }
        return status_map.get(doc_status.lower(), 'Uploaded')
    
    def _extract_original_filename(self, unique_filename: str) -> str:
        """Extract original filename from unique filename"""
        # Format: originalname_20240101_123456_abcd1234.ext
        parts = unique_filename.split('_')
        if len(parts) >= 3:
            # Check if second part looks like a date (8 digits)
            if parts[1].isdigit() and len(parts[1]) == 8:
                extension = Path(unique_filename).suffix
                return parts[0] + extension
        return unique_filename
    
    def _get_mime_type_from_filename(self, filename: str) -> str:
        """Determine MIME type based on file extension"""
        extension = Path(filename).suffix.lower()
        mime_map = {
            '.pdf': 'application/pdf',
            '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            '.doc': 'application/msword',
            '.txt': 'text/plain',
            '.md': 'text/markdown',
            '.png': 'image/png',
            '.jpg': 'image/jpeg',
            '.jpeg': 'image/jpeg',
            '.gif': 'image/gif'
        }
        return mime_map.get(extension, 'application/octet-stream')

    def save_document(self, document: Document) -> int:
        """Save a new document and return its ID, with automatic Files table sync"""
        try:
            doc_id = self._execute_with_retry("""
                INSERT INTO documents 
                (filename, original_filename, file_path, file_size, mime_type, upload_date, processed, processing_status)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                document.filename,
                document.original_filename,
                document.file_path,
                document.file_size,
                document.mime_type,
                document.upload_date,
                document.processed,
                document.processing_status
            ));
            
            # Also create corresponding Files record for C# compatibility
            self._sync_document_to_files(doc_id, document);
            
            logger.debug(f"Saved document with ID {doc_id}")
            return doc_id
        except Exception as e:
            logger.error(f"Error saving document: {e}")
            raise

    def update_processing_status(self, doc_id: int, status: str) -> None:
        """Update the processing status of a document and sync to Files table"""
        try:
            self._execute_with_retry("""
                UPDATE documents 
                SET processing_status = ?, processed = ?, upload_date = COALESCE(upload_date, CURRENT_TIMESTAMP)
                WHERE id = ?
            """, (status, status == "completed", doc_id))
            
            # Update corresponding Files record
            self._sync_document_status_to_files(doc_id, status)
            
            logger.debug(f"Updated document {doc_id} status to {status}")
        except Exception as e:
            logger.error(f"Error updating document {doc_id} status: {e}")
            raise

    def _sync_document_to_files(self, document_id: int, document: Document):
        """Create or update Files record for a document"""
        try:
            file_status = self._convert_document_status_to_file_status(document.processing_status)
            
            # Check if bridge record exists
            bridge_row = self._execute_with_retry("""
                SELECT file_id FROM file_document_bridge WHERE document_id = ?
            """, (document_id,), fetch_one=True, read_only=True)
            
            if bridge_row:
                # Update existing Files record
                file_id = bridge_row[0]
                self._execute_with_retry("""
                    UPDATE Files 
                    SET FileName = ?, Size = ?, UploadedAt = ?, Status = ?
                    WHERE Id = ?
                """, (
                    document.filename,
                    document.file_size or 0,
                    document.upload_date,
                    file_status,
                    file_id
                ))
            else:
                # Create new Files record
                file_id = self._execute_with_retry("""
                    INSERT INTO Files (FileName, Size, UploadedAt, Status, FileHash)
                    VALUES (?, ?, ?, ?, ?)
                """, (
                    document.filename,
                    document.file_size or 0,
                    document.upload_date,
                    file_status,
                    ''  # Empty hash for now
                ))
                
                # Create bridge record
                self._execute_with_retry("""
                    INSERT INTO file_document_bridge (file_id, document_id, sync_status)
                    VALUES (?, ?, 'synced')
                """, (file_id, document_id))
                
        except Exception as e:
            logger.debug(f"Could not sync document {document_id} to Files table: {e}")

    def _sync_document_status_to_files(self, document_id: int, processing_status: str):
        """Update Files table status when document status changes"""
        try:
            file_status = self._convert_document_status_to_file_status(processing_status)
            
            self._execute_with_retry("""
                UPDATE Files 
                SET Status = ?
                WHERE Id IN (
                    SELECT file_id FROM file_document_bridge WHERE document_id = ?
                )
            """, (file_status, document_id))
            
        except Exception as e:
            logger.debug(f"Could not sync document {document_id} status to Files table: {e}")

    def get_file_document_sync_status(self) -> Dict[str, Any]:
        """Get synchronization status between Files and documents tables"""
        try:
            # Count records in each table
            files_count = self._execute_with_retry(
                "SELECT COUNT(*) FROM Files", 
                fetch_one=True, read_only=True
            )[0]
            
            documents_count = self._execute_with_retry(
                "SELECT COUNT(*) FROM documents", 
                fetch_one=True, read_only=True
            )[0]
            
            # Count bridge records
            bridge_count = self._execute_with_retry(
                "SELECT COUNT(*) FROM file_document_bridge", 
                fetch_one=True, read_only=True
            )[0]
            
            # Find unsynced records
            unsynced_files = self._execute_with_retry("""
                SELECT COUNT(*) FROM Files f
                LEFT JOIN file_document_bridge fdb ON f.Id = fdb.file_id
                WHERE fdb.file_id IS NULL
            """, fetch_one=True, read_only=True)[0]
            
            unsynced_documents = self._execute_with_retry("""
                SELECT COUNT(*) FROM documents d
                LEFT JOIN file_document_bridge fdb ON d.id = fdb.document_id
                WHERE fdb.document_id IS NULL
            """, fetch_one=True, read_only=True)[0]
            
            sync_status = {
                "files_count": files_count,
                "documents_count": documents_count,
                "bridge_records": bridge_count,
                "unsynced_files": unsynced_files,
                "unsynced_documents": unsynced_documents,
                "sync_health": "healthy" if unsynced_files == 0 and unsynced_documents == 0 else "needs_sync",
                "recommendations": []
            }
            
            if unsynced_files > 0:
                sync_status["recommendations"].append(f"Sync {unsynced_files} Files records to documents table")
            
            if unsynced_documents > 0:
                sync_status["recommendations"].append(f"Sync {unsynced_documents} documents records to Files table")
            
            return sync_status
            
        except Exception as e:
            logger.error(f"Error getting sync status: {e}")
            return {
                "error": str(e),
                "sync_health": "error"
            }

    def force_sync_files_and_documents(self) -> Dict[str, Any]:
        """Force synchronization between Files and documents tables"""
        try:
            with self._pool.get_connection() as conn:
                # Perform sync
                self._sync_files_and_documents(conn)
                conn.commit()
                
                # Get updated sync status
                sync_status = self.get_file_document_sync_status()
                sync_status["sync_performed"] = True
                sync_status["timestamp"] = datetime.now().isoformat()
                
                return sync_status
                
        except Exception as e:
            logger.error(f"Error forcing sync: {e}")
            return {
                "error": str(e),
                "sync_performed": False,
                "timestamp": datetime.now().isoformat()
            }

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

    def get_all_documents(self) -> list:
        """Return all documents from the database as a list of Document objects. Logs fetched rows for debugging."""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("SELECT id, filename, original_filename, file_path, file_size, mime_type, upload_date, processed, processing_status FROM documents")
                rows = cursor.fetchall()
                logger.info(f"Fetched rows: {rows}")  # Debug log
                return [
                    Document(
                        id=row[0],
                        filename=row[1],
                        original_filename=row[2],
                        file_path=row[3],
                        file_size=row[4],
                        mime_type=row[5],
                        upload_date=row[6],
                        processed=row[7],
                        processing_status=row[8]
                    )
                    for row in rows
                ]
        except Exception as e:
            logger.error(f"Error fetching all documents: {e}")
            raise