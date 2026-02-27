#!/usr/bin/env python3
"""
Quick database fix script for the SQLite I/O error

This script creates a working database in an accessible location.
"""

import sqlite3
import os
import json
from pathlib import Path

def create_working_database():
    """Create a working database in a location with proper permissions"""
    
    # Try different locations in order of preference
    db_locations = [
        "/app/database/data-resources.db",
        "/tmp/aspire_database/data-resources.db", 
        "/tmp/data-resources.db"
    ]
    
    for db_path in db_locations:
        try:
            print(f"?? Trying to create database at: {db_path}")
            
            # Ensure directory exists
            db_dir = Path(db_path).parent
            db_dir.mkdir(parents=True, exist_ok=True)
            
            # Test write access
            test_file = db_dir / ".write_test"
            test_file.touch()
            test_file.unlink()
            
            # Create database
            with sqlite3.connect(db_path) as conn:
                cursor = conn.cursor()
                
                # Create schema
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
                
                conn.commit()
                
                # Test operations
                cursor.execute("SELECT COUNT(*) FROM documents")
                cursor.fetchone()
                
                print(f"? Database created successfully at: {db_path}")
                
                # Save working path
                os.environ['ASPIRE_DB_PATH'] = db_path
                
                return db_path
                
        except Exception as e:
            print(f"? Failed to create database at {db_path}: {e}")
            continue
    
    raise RuntimeError("Could not create database at any location")

if __name__ == "__main__":
    try:
        working_db = create_working_database()
        print(f"\n?? Database ready at: {working_db}")
        print("The Python service should now work correctly.")
    except Exception as e:
        print(f"\n? Database creation failed: {e}")
        exit(1)