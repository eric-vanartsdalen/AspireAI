#!/usr/bin/env python3
"""
Database troubleshooting and initialization script

This script helps diagnose and fix SQLite database issues in the AspireAI Python service.
"""

import os
import sqlite3
import sys
import json
from pathlib import Path
import logging

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

def check_database_directory(db_path="/app/database/data-resources.db"):
    """Check if database directory exists and is writable"""
    logger.info("?? Checking database directory...")
    
    db_dir = Path(db_path).parent
    db_file = Path(db_path)
    
    print(f"Database path: {db_path}")
    print(f"Database directory: {db_dir}")
    print(f"Database file exists: {db_file.exists()}")
    print(f"Database directory exists: {db_dir.exists()}")
    
    # Check directory permissions
    if db_dir.exists():
        print(f"Directory is readable: {os.access(db_dir, os.R_OK)}")
        print(f"Directory is writable: {os.access(db_dir, os.W_OK)}")
        print(f"Directory is executable: {os.access(db_dir, os.X_OK)}")
    else:
        print("? Database directory does not exist")
        return False
    
    # Check file permissions if it exists
    if db_file.exists():
        print(f"File is readable: {os.access(db_file, os.R_OK)}")
        print(f"File is writable: {os.access(db_file, os.W_OK)}")
        print(f"File size: {db_file.stat().st_size} bytes")
    
    return True

def test_database_connection(db_path="/app/database/data-resources.db"):
    """Test SQLite database connection"""
    logger.info("?? Testing database connection...")
    
    try:
        with sqlite3.connect(db_path, timeout=10.0) as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT 1")
            result = cursor.fetchone()
            if result[0] == 1:
                print("? Database connection successful")
                return True
            else:
                print("? Database connection test failed")
                return False
    except Exception as e:
        print(f"? Database connection failed: {e}")
        return False

def check_database_schema(db_path="/app/database/data-resources.db"):
    """Check if database schema exists"""
    logger.info("?? Checking database schema...")
    
    try:
        with sqlite3.connect(db_path) as conn:
            cursor = conn.cursor()
            
            # Check if tables exist
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
            tables = [row[0] for row in cursor.fetchall()]
            
            expected_tables = ['documents', 'processed_documents', 'document_pages']
            
            print(f"Existing tables: {tables}")
            print(f"Expected tables: {expected_tables}")
            
            missing_tables = [table for table in expected_tables if table not in tables]
            if missing_tables:
                print(f"? Missing tables: {missing_tables}")
                return False
            else:
                print("? All required tables exist")
                
                # Check table structure
                for table in expected_tables:
                    cursor.execute(f"PRAGMA table_info({table})")
                    columns = cursor.fetchall()
                    print(f"Table {table} columns: {len(columns)}")
                
                return True
                
    except Exception as e:
        print(f"? Schema check failed: {e}")
        return False

def create_database_schema(db_path="/app/database/data-resources.db"):
    """Create database schema"""
    logger.info("?? Creating database schema...")
    
    try:
        # Ensure directory exists
        db_dir = Path(db_path).parent
        db_dir.mkdir(parents=True, exist_ok=True)
        
        with sqlite3.connect(db_path) as conn:
            cursor = conn.cursor()
            
            # Create documents table
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
            
            # Create processed_documents table
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
            
            # Create document_pages table
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
            
            # Create indexes
            cursor.execute("CREATE INDEX IF NOT EXISTS idx_documents_processed ON documents(processed)")
            cursor.execute("CREATE INDEX IF NOT EXISTS idx_documents_upload_date ON documents(upload_date)")
            
            conn.commit()
            print("? Database schema created successfully")
            return True
            
    except Exception as e:
        print(f"? Schema creation failed: {e}")
        return False

def insert_test_data(db_path="/app/database/data-resources.db"):
    """Insert test data to verify database works"""
    logger.info("?? Inserting test data...")
    
    try:
        with sqlite3.connect(db_path) as conn:
            cursor = conn.cursor()
            
            # Insert test document
            cursor.execute("""
                INSERT OR IGNORE INTO documents 
                (filename, original_filename, file_path, file_size, mime_type)
                VALUES (?, ?, ?, ?, ?)
            """, ("test_doc.pdf", "test_document.pdf", "test_doc.pdf", 1024, "application/pdf"))
            
            conn.commit()
            
            # Verify insertion
            cursor.execute("SELECT COUNT(*) FROM documents")
            count = cursor.fetchone()[0]
            print(f"? Test data inserted. Total documents: {count}")
            return True
            
    except Exception as e:
        print(f"? Test data insertion failed: {e}")
        return False

def fix_permissions(db_path="/app/database/data-resources.db"):
    """Try to fix file permissions"""
    logger.info("?? Attempting to fix permissions...")
    
    try:
        db_dir = Path(db_path).parent
        db_file = Path(db_path)
        
        # Create directory if it doesn't exist
        if not db_dir.exists():
            db_dir.mkdir(parents=True, exist_ok=True)
            print(f"? Created directory: {db_dir}")
        
        # Set directory permissions
        os.chmod(db_dir, 0o755)
        print(f"? Set directory permissions: {db_dir}")
        
        # Set file permissions if file exists
        if db_file.exists():
            os.chmod(db_file, 0o644)
            print(f"? Set file permissions: {db_file}")
        
        return True
        
    except Exception as e:
        print(f"? Permission fix failed: {e}")
        return False

def main():
    """Main diagnostic function"""
    print("?? AspireAI Database Diagnostic Tool")
    print("=" * 50)
    
    # Check if we're running in container
    is_container = os.path.exists("/.dockerenv")
    print(f"Running in container: {is_container}")
    
    # Define database path
    db_path = "/app/database/data-resources.db"
    
    # Alternative paths to try
    alt_paths = [
        "/tmp/aspire_database/data-resources.db",
        "/tmp/data-resources.db",
        "./database/data-resources.db"
    ]
    
    print("\n1. Checking database directory and permissions...")
    directory_ok = check_database_directory(db_path)
    
    if not directory_ok:
        print("\n?? Attempting to fix permissions...")
        fix_permissions(db_path)
        directory_ok = check_database_directory(db_path)
    
    print("\n2. Testing database connection...")
    connection_ok = test_database_connection(db_path)
    
    if not connection_ok:
        print("\n?? Trying alternative database paths...")
        for alt_path in alt_paths:
            print(f"Trying: {alt_path}")
            Path(alt_path).parent.mkdir(parents=True, exist_ok=True)
            if test_database_connection(alt_path):
                print(f"? Alternative path works: {alt_path}")
                db_path = alt_path
                connection_ok = True
                break
    
    if not connection_ok:
        print("? Could not establish database connection with any path")
        return False
    
    print("\n3. Checking database schema...")
    schema_ok = check_database_schema(db_path)
    
    if not schema_ok:
        print("\n?? Creating database schema...")
        schema_ok = create_database_schema(db_path)
    
    if schema_ok:
        print("\n4. Testing database operations...")
        test_ok = insert_test_data(db_path)
        
        if test_ok:
            print(f"\n?? Database is fully functional at: {db_path}")
            print("\n?? Recommendations:")
            print(f"   1. Use this database path: {db_path}")
            print("   2. Ensure proper volume mounting in Docker")
            print("   3. Check that the container has write permissions")
            
            # Save working path for the service
            config = {"working_database_path": db_path}
            config_path = Path("/tmp/db_config.json")
            with open(config_path, 'w') as f:
                json.dump(config, f)
            print(f"   4. Configuration saved to: {config_path}")
            
            return True
    
    print("\n? Database diagnostic failed")
    return False

if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)