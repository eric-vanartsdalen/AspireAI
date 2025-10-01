#!/usr/bin/env python3
"""
Database migration tool to add Python service compatible tables to existing .NET database
"""

import sqlite3
import sys
import os
from datetime import datetime

def migrate_database(db_path="../database/data-resources.db"):
    """Add the required tables for Python service compatibility"""
    
    try:
        with sqlite3.connect(db_path) as conn:
            cursor = conn.cursor()
            
            print(f"?? Starting database migration for: {db_path}")
            
            # Check existing tables
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
            existing_tables = [row[0] for row in cursor.fetchall()]
            print(f"?? Existing tables: {existing_tables}")
            
            # Create documents table
            if 'documents' not in existing_tables:
                print("?? Creating 'documents' table...")
                cursor.execute("""
                    CREATE TABLE documents (
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
                
                # Create indexes
                cursor.execute("CREATE INDEX idx_documents_processed ON documents(processed)")
                cursor.execute("CREATE INDEX idx_documents_upload_date ON documents(upload_date)")
                print("? Created 'documents' table with indexes")
            else:
                print("? 'documents' table already exists")
            
            # Create processed_documents table
            if 'processed_documents' not in existing_tables:
                print("?? Creating 'processed_documents' table...")
                cursor.execute("""
                    CREATE TABLE processed_documents (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        document_id INTEGER REFERENCES documents(id),
                        docling_document_path TEXT NOT NULL,
                        total_pages INTEGER,
                        processing_date DATETIME DEFAULT CURRENT_TIMESTAMP,
                        processing_metadata TEXT,
                        neo4j_node_id TEXT
                    )
                """)
                
                # Create indexes
                cursor.execute("CREATE INDEX idx_processed_documents_document_id ON processed_documents(document_id)")
                print("? Created 'processed_documents' table with indexes")
            else:
                print("? 'processed_documents' table already exists")
            
            # Create document_pages table
            if 'document_pages' not in existing_tables:
                print("?? Creating 'document_pages' table...")
                cursor.execute("""
                    CREATE TABLE document_pages (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        processed_document_id INTEGER REFERENCES processed_documents(id),
                        page_number INTEGER NOT NULL,
                        content TEXT NOT NULL,
                        page_metadata TEXT,
                        neo4j_node_id TEXT
                    )
                """)
                
                # Create indexes
                cursor.execute("CREATE INDEX idx_document_pages_processed_doc_id ON document_pages(processed_document_id)")
                cursor.execute("CREATE INDEX idx_document_pages_page_number ON document_pages(page_number)")
                print("? Created 'document_pages' table with indexes")
            else:
                print("? 'document_pages' table already exists")
            
            conn.commit()
            
            # Migrate existing Files data to Documents if any exists
            if 'Files' in existing_tables:
                print("?? Checking for data to migrate from 'Files' to 'documents'...")
                
                # Check if Files table has data
                cursor.execute("SELECT COUNT(*) FROM Files")
                files_count = cursor.fetchone()[0]
                
                if files_count > 0:
                    print(f"?? Found {files_count} files to migrate")
                    
                    # Check if documents table is empty
                    cursor.execute("SELECT COUNT(*) FROM documents")
                    docs_count = cursor.fetchone()[0]
                    
                    if docs_count == 0:
                        print("?? Migrating Files data to documents table...")
                        
                        # Migrate data
                        cursor.execute("""
                            INSERT INTO documents 
                            (filename, original_filename, file_path, file_size, mime_type, upload_date, processed, processing_status)
                            SELECT 
                                FileName,
                                FileName,
                                FileName,
                                Size,
                                CASE 
                                    WHEN LOWER(FileName) LIKE '%.pdf' THEN 'application/pdf'
                                    WHEN LOWER(FileName) LIKE '%.docx' THEN 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
                                    WHEN LOWER(FileName) LIKE '%.doc' THEN 'application/msword'
                                    WHEN LOWER(FileName) LIKE '%.txt' THEN 'text/plain'
                                    WHEN LOWER(FileName) LIKE '%.md' THEN 'text/markdown'
                                    ELSE 'application/octet-stream'
                                END,
                                UploadedAt,
                                CASE WHEN Status = 'Processed' THEN 1 ELSE 0 END,
                                CASE 
                                    WHEN Status = 'Uploaded' THEN 'pending'
                                    WHEN Status = 'Processed' THEN 'completed'
                                    WHEN Status = 'Error' THEN 'error'
                                    ELSE 'pending'
                                END
                            FROM Files
                        """)
                        
                        migrated_count = cursor.rowcount
                        conn.commit()
                        print(f"? Migrated {migrated_count} files to documents table")
                    else:
                        print(f"?? Documents table already has {docs_count} records, skipping migration")
                else:
                    print("?? No files to migrate")
            
            print(f"\n?? Database migration completed successfully!")
            
            # Final verification
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
            final_tables = [row[0] for row in cursor.fetchall()]
            
            required_tables = ['documents', 'processed_documents', 'document_pages']
            missing_tables = [table for table in required_tables if table not in final_tables]
            
            if not missing_tables:
                print(f"? All required tables verified: {required_tables}")
                
                # Show final counts
                for table in final_tables:
                    cursor.execute(f"SELECT COUNT(*) FROM {table}")
                    count = cursor.fetchone()[0]
                    print(f"   - {table}: {count} records")
                
                return True
            else:
                print(f"? Missing tables after migration: {missing_tables}")
                return False
                
    except Exception as e:
        print(f"? Migration failed: {e}")
        return False

def backup_database(db_path):
    """Create a backup of the database before migration"""
    backup_path = f"{db_path}.backup_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
    
    try:
        import shutil
        shutil.copy2(db_path, backup_path)
        print(f"?? Database backed up to: {backup_path}")
        return backup_path
    except Exception as e:
        print(f"?? Failed to create backup: {e}")
        return None

def main():
    """Main migration function"""
    print("?? AspireAI Database Migration Tool")
    print("=" * 50)
    print("This tool adds Python service compatible tables to your existing .NET database.")
    print()
    
    # Find database
    db_paths = [
        "../database/data-resources.db",
        "database/data-resources.db", 
        "src/AspireApp.Web/data-resources.db"
    ]
    
    db_path = None
    for path in db_paths:
        if os.path.exists(path):
            db_path = path
            break
    
    if not db_path:
        print("? Database file not found. Expected locations:")
        for path in db_paths:
            print(f"   - {path}")
        print("\n?? Please ensure your Aspire application has been started at least once to create the database.")
        sys.exit(1)
    
    print(f"?? Found database at: {db_path}")
    
    # Ask for confirmation
    response = input(f"\n?? Do you want to migrate this database? (y/N): ").lower().strip()
    if response not in ['y', 'yes']:
        print("?? Migration cancelled.")
        sys.exit(0)
    
    # Create backup
    backup_path = backup_database(db_path)
    
    # Perform migration
    success = migrate_database(db_path)
    
    if success:
        print(f"\n?? Migration completed successfully!")
        print(f"\n?? What's next:")
        print(f"   1. Start your Aspire application")
        print(f"   2. The Python service should no longer get database errors")
        print(f"   3. Test the /documents/ endpoint: curl http://localhost:8000/documents/")
        print(f"   4. Check bridge health: curl http://localhost:5000/api/documentbridge/health")
        
        if backup_path:
            print(f"\n?? Backup created at: {backup_path}")
            print(f"   (You can delete this backup once everything is working)")
    else:
        print(f"\n? Migration failed!")
        if backup_path:
            print(f"?? Your original database is backed up at: {backup_path}")

if __name__ == "__main__":
    main()