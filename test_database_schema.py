#!/usr/bin/env python3
"""
Quick test script to verify that the .NET database creates the expected tables for the Python service
"""

import sqlite3
import sys
import os
from pathlib import Path

def test_database_schema(db_path="../database/data-resources.db"):
    """Test if the database has the expected schema for Python service compatibility"""
    
    if not os.path.exists(db_path):
        print(f"? Database file not found: {db_path}")
        return False
    
    try:
        with sqlite3.connect(db_path) as conn:
            cursor = conn.cursor()
            
            # Get all tables
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
            tables = [row[0] for row in cursor.fetchall()]
            
            print(f"?? Found tables in database: {tables}")
            
            # Expected tables for Python service
            expected_tables = ['documents', 'processed_documents', 'document_pages']
            existing_tables = ['Files']  # Original .NET table
            
            # Check for expected tables
            missing_tables = [table for table in expected_tables if table not in tables]
            
            if missing_tables:
                print(f"? Missing required tables: {missing_tables}")
                return False
            
            print("? All required tables found!")
            
            # Check table schemas
            for table in expected_tables:
                cursor.execute(f"PRAGMA table_info({table})")
                columns = cursor.fetchall()
                print(f"\n?? Table '{table}' schema:")
                for col in columns:
                    print(f"  - {col[1]} ({col[2]}) {'NOT NULL' if col[3] else ''} {'PRIMARY KEY' if col[5] else ''}")
            
            # Check data counts
            print(f"\n?? Data counts:")
            for table in tables:
                try:
                    cursor.execute(f"SELECT COUNT(*) FROM {table}")
                    count = cursor.fetchone()[0]
                    print(f"  - {table}: {count} records")
                except Exception as e:
                    print(f"  - {table}: Error counting records ({e})")
            
            # Test basic operations
            print(f"\n?? Testing basic operations...")
            
            # Test Documents table operations
            try:
                cursor.execute("""
                    INSERT INTO documents 
                    (filename, original_filename, file_path, file_size, mime_type) 
                    VALUES (?, ?, ?, ?, ?)
                """, ("test.pdf", "test.pdf", "test.pdf", 1024, "application/pdf"))
                
                cursor.execute("SELECT id FROM documents WHERE filename = ?", ("test.pdf",))
                doc_id = cursor.fetchone()[0]
                
                print(f"  ? Documents table insert/select: Document ID {doc_id}")
                
                # Clean up test data
                cursor.execute("DELETE FROM documents WHERE filename = ?", ("test.pdf",))
                
            except Exception as e:
                print(f"  ? Documents table operation failed: {e}")
                return False
            
            print(f"\n?? Database schema verification successful!")
            print(f"?? The Python service should now be able to access the database properly.")
            
            return True
            
    except Exception as e:
        print(f"? Database test failed: {e}")
        return False

def main():
    """Main test function"""
    print("?? Testing .NET Database Schema for Python Service Compatibility")
    print("=" * 70)
    
    # Test the database
    db_paths = [
        "../database/data-resources.db",
        "database/data-resources.db", 
        "src/AspireApp.Web/data-resources.db"
    ]
    
    for db_path in db_paths:
        if os.path.exists(db_path):
            print(f"?? Found database at: {db_path}")
            success = test_database_schema(db_path)
            
            if success:
                print(f"\n? Database test passed! Python service should work with this database.")
                print(f"\n?? Next steps:")
                print(f"   1. Start your Aspire application")
                print(f"   2. The Python service should no longer get 'disk I/O error'")
                print(f"   3. Test the /documents/ endpoint")
                print(f"   4. Upload files through the Blazor interface")
                return
            else:
                print(f"\n? Database test failed for {db_path}")
        else:
            print(f"?? Database not found at: {db_path}")
    
    print(f"\n?? If no database was found:")
    print(f"   1. Start your Aspire application first")
    print(f"   2. This will create the database with the correct schema")
    print(f"   3. Then run this test again")

if __name__ == "__main__":
    main()