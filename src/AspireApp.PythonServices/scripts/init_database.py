#!/usr/bin/env python3
"""
Database initialization script for AspireAI Python Services.
This script helps diagnose and fix database permission issues.
"""

import os
import sys
import sqlite3
import stat
import logging
from pathlib import Path

# Add the app directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)


def check_directory_permissions(path):
    """Check if directory exists and is writable"""
    path_obj = Path(path)
    
    logger.info(f"Checking directory: {path}")
    logger.info(f"  Exists: {path_obj.exists()}")
    
    if path_obj.exists():
        stat_info = path_obj.stat()
        logger.info(f"  Owner: {stat_info.st_uid}")
        logger.info(f"  Group: {stat_info.st_gid}")
        logger.info(f"  Permissions: {oct(stat_info.st_mode)}")
        
        # Test write access
        test_file = path_obj / ".write_test"
        try:
            test_file.touch()
            test_file.unlink()
            logger.info(f"  Writable: Yes")
            return True
        except Exception as e:
            logger.error(f"  Writable: No - {e}")
            return False
    else:
        try:
            path_obj.mkdir(parents=True, exist_ok=True)
            logger.info(f"  Created directory: {path}")
            return check_directory_permissions(path)
        except Exception as e:
            logger.error(f"  Could not create directory: {e}")
            return False


def test_database_creation(db_path):
    """Test SQLite database creation and operations"""
    logger.info(f"Testing database operations at: {db_path}")
    
    try:
        # Test basic connection
        conn = sqlite3.connect(db_path, timeout=10.0)
        cursor = conn.cursor()
        
        # Test table creation
        cursor.execute("CREATE TABLE IF NOT EXISTS test_table (id INTEGER PRIMARY KEY, data TEXT)")
        
        # Test WAL mode
        try:
            cursor.execute("PRAGMA journal_mode=WAL")
            result = cursor.fetchone()
            logger.info(f"  WAL mode result: {result}")
        except Exception as e:
            logger.warning(f"  WAL mode failed: {e}")
        
        # Test insert/select
        cursor.execute("INSERT OR IGNORE INTO test_table (data) VALUES (?)", ("test_data",))
        cursor.execute("SELECT COUNT(*) FROM test_table")
        count = cursor.fetchone()[0]
        logger.info(f"  Test table has {count} rows")
        
        # Cleanup
        cursor.execute("DROP TABLE test_table")
        conn.commit()
        conn.close()
        
        logger.info("  Database operations: SUCCESS")
        return True
        
    except Exception as e:
        logger.error(f"  Database operations failed: {e}")
        return False


def main():
    """Main diagnostic function"""
    logger.info("=== AspireAI Database Diagnostic Tool ===")
    
    # Get current user info
    logger.info(f"Running as UID: {os.getuid()}, GID: {os.getgid()}")
    
    # Test possible database paths
    test_paths = [
        "/app/database",
        "/tmp/aspire_database",
        "/tmp"
    ]
    
    working_paths = []
    
    for path in test_paths:
        if check_directory_permissions(path):
            working_paths.append(path)
    
    if not working_paths:
        logger.error("No writable directories found!")
        return False
    
    logger.info(f"Working directories: {working_paths}")
    
    # Test database creation in the first working directory
    db_path = os.path.join(working_paths[0], "test-database.db")
    success = test_database_creation(db_path)
    
    if success:
        # Clean up test database
        try:
            os.unlink(db_path)
        except:
            pass
    
    # Test the actual database service
    try:
        logger.info("Testing DatabaseService initialization...")
        from app.services.database_service import DatabaseService
        
        db_service = DatabaseService()
        health = db_service.health_check()
        
        if health["status"] == "healthy":
            logger.info("DatabaseService: SUCCESS")
            logger.info(f"  Using database: {health['database_path']}")
            logger.info(f"  Document count: {health['document_count']}")
        else:
            logger.error(f"DatabaseService: FAILED - {health}")
            
    except Exception as e:
        logger.error(f"DatabaseService initialization failed: {e}")
        return False
    
    logger.info("=== Diagnostic Complete ===")
    return True


if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)