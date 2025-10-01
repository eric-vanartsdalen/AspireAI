#!/usr/bin/env python3
"""
Database initialization and schema fix script for AspireAI
Ensures both Files and documents tables exist and are properly synchronized
"""

import sys
import os
from pathlib import Path

# Add the parent directory to the path to import our modules
sys.path.append(str(Path(__file__).parent.parent))

from app.services.database_service import DatabaseService
from app.models.models import Document
from datetime import datetime


def check_schema_status():
    """Check the current schema status"""
    print("?? Checking database schema status...")
    
    try:
        db_service = DatabaseService()
        
        # Get basic health info
        health = db_service.health_check()
        print(f"? Database health: {health.get('status', 'unknown')}")
        print(f"?? Database path: {health.get('database_path', 'unknown')}")
        
        # Get sync status
        sync_status = db_service.get_file_document_sync_status()
        
        print("\n?? Schema Synchronization Status:")
        print(f"   Files table records: {sync_status.get('files_count', 0)}")
        print(f"   Documents table records: {sync_status.get('documents_count', 0)}")
        print(f"   Bridge records: {sync_status.get('bridge_records', 0)}")
        print(f"   Unsynced Files: {sync_status.get('unsynced_files', 0)}")
        print(f"   Unsynced Documents: {sync_status.get('unsynced_documents', 0)}")
        print(f"   Sync Health: {sync_status.get('sync_health', 'unknown')}")
        
        if sync_status.get('recommendations'):
            print("\n?? Recommendations:")
            for rec in sync_status['recommendations']:
                print(f"   • {rec}")
        
        return sync_status
        
    except Exception as e:
        print(f"? Error checking schema: {e}")
        return None


def fix_schema():
    """Fix schema issues by forcing synchronization"""
    print("\n?? Attempting to fix schema issues...")
    
    try:
        db_service = DatabaseService()
        
        # Force sync
        sync_result = db_service.force_sync_files_and_documents()
        
        if sync_result.get('sync_performed'):
            print("? Schema synchronization completed successfully!")
            
            # Show updated status
            print("\n?? Updated Schema Status:")
            updated_sync = sync_result
            print(f"   Files table records: {updated_sync.get('files_count', 0)}")
            print(f"   Documents table records: {updated_sync.get('documents_count', 0)}")
            print(f"   Bridge records: {updated_sync.get('bridge_records', 0)}")
            print(f"   Sync Health: {updated_sync.get('sync_health', 'unknown')}")
            
            return True
        else:
            print(f"? Schema synchronization failed: {sync_result.get('error', 'Unknown error')}")
            return False
            
    except Exception as e:
        print(f"? Error fixing schema: {e}")
        return False


def create_test_data():
    """Create some test data to verify schema works"""
    print("\n?? Creating test data...")
    
    try:
        db_service = DatabaseService()
        
        # Create a test document
        test_doc = Document(
            id=0,  # Will be auto-generated
            filename="test_schema_fix.pdf",
            original_filename="test_schema_fix.pdf",
            file_path="/test/test_schema_fix.pdf",
            file_size=1024,
            mime_type="application/pdf",
            upload_date=datetime.now(),
            processed=False,
            processing_status="pending"
        )
        
        doc_id = db_service.save_document(test_doc)
        print(f"? Created test document with ID: {doc_id}")
        
        # Update its status to test sync
        db_service.update_processing_status(doc_id, "processing")
        print(f"? Updated test document status to 'processing'")
        
        db_service.update_processing_status(doc_id, "completed")
        print(f"? Updated test document status to 'completed'")
        
        # Verify sync
        sync_status = db_service.get_file_document_sync_status()
        if sync_status.get('sync_health') == 'healthy':
            print("? Schema sync is working correctly!")
        else:
            print(f"??  Schema sync health: {sync_status.get('sync_health')}")
        
        return True
        
    except Exception as e:
        print(f"? Error creating test data: {e}")
        return False


def show_table_info():
    """Show detailed information about database tables"""
    print("\n?? Database Table Information:")
    
    try:
        db_service = DatabaseService()
        
        with db_service._pool.get_connection() as conn:
            cursor = conn.cursor()
            
            # Get table names
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
            tables = cursor.fetchall()
            
            print(f"\n?? Tables found: {len(tables)}")
            for table in tables:
                table_name = table[0]
                print(f"\n   ???  {table_name}:")
                
                # Get table schema
                cursor.execute(f"PRAGMA table_info({table_name})")
                columns = cursor.fetchall()
                
                for col in columns:
                    col_id, name, col_type, not_null, default, pk = col
                    pk_marker = " (PK)" if pk else ""
                    null_marker = " NOT NULL" if not_null else ""
                    default_info = f" DEFAULT {default}" if default else ""
                    print(f"      • {name}: {col_type}{pk_marker}{null_marker}{default_info}")
                
                # Get row count
                cursor.execute(f"SELECT COUNT(*) FROM {table_name}")
                count = cursor.fetchone()[0]
                print(f"      ?? Records: {count}")
        
        return True
        
    except Exception as e:
        print(f"? Error getting table info: {e}")
        return False


def main():
    """Main function to run schema diagnostics and fixes"""
    print("?? AspireAI Database Schema Fix Tool")
    print("=" * 50)
    
    # Check current status
    sync_status = check_schema_status()
    
    if not sync_status:
        print("\n? Could not check schema status. Exiting.")
        return False
    
    # Show detailed table information
    show_table_info()
    
    # Check if fix is needed
    needs_fix = (
        sync_status.get('sync_health') != 'healthy' or
        sync_status.get('unsynced_files', 0) > 0 or
        sync_status.get('unsynced_documents', 0) > 0
    )
    
    if needs_fix:
        print(f"\n??  Schema issues detected. Attempting to fix...")
        
        if fix_schema():
            print("\n? Schema fix completed successfully!")
        else:
            print("\n? Schema fix failed!")
            return False
    else:
        print("\n? Schema is healthy, no fixes needed!")
    
    # Test with sample data
    print(f"\n?? Testing schema with sample data...")
    if create_test_data():
        print("\n? Schema testing completed successfully!")
    else:
        print("\n? Schema testing failed!")
        return False
    
    # Final status check
    print(f"\n?? Final Status Check:")
    final_status = check_schema_status()
    
    if final_status and final_status.get('sync_health') == 'healthy':
        print("\n?? Database schema is now healthy and ready for use!")
        print("\n?? Your C# service should now be able to start without schema errors.")
        return True
    else:
        print("\n? Schema issues still exist. Manual intervention may be required.")
        return False


if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Fix AspireAI database schema issues")
    parser.add_argument("--check-only", action="store_true", 
                       help="Only check schema status, don't fix")
    parser.add_argument("--force-fix", action="store_true",
                       help="Force schema synchronization")
    parser.add_argument("--show-tables", action="store_true",
                       help="Show detailed table information")
    
    args = parser.parse_args()
    
    if args.check_only:
        check_schema_status()
    elif args.force_fix:
        fix_schema()
    elif args.show_tables:
        show_table_info()
    else:
        success = main()
        sys.exit(0 if success else 1)