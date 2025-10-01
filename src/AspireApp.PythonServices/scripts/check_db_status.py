#!/usr/bin/env python3
"""
Database monitoring utility for AspireAI Python Services.
"""

import os
import sys
import json
from pathlib import Path

# Add the app directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))

def check_database_status():
    """Check current database status and locations"""
    print("=== AspireAI Database Status ===")
    
    # Check environment variables
    env_path = os.getenv('ASPIRE_DB_PATH')
    backup_path = os.getenv('ASPIRE_DB_BACKUP_PATH')
    
    print(f"Environment DB Path: {env_path}")
    print(f"Environment Backup Path: {backup_path}")
    print()
    
    # Check possible database locations
    locations = [
        "/app/database/data-resources.db",
        "/app/host-database/data-resources.db", 
        "/tmp/aspire_database/data-resources.db",
        "/tmp/data-resources.db"
    ]
    
    print("Database file locations:")
    for location in locations:
        path = Path(location)
        if path.exists():
            size = path.stat().st_size
            modified = path.stat().st_mtime
            print(f"  ? {location} (size: {size} bytes, modified: {modified})")
        else:
            print(f"  ? {location} (not found)")
    print()
    
    # Test database service
    try:
        from app.services.database_service import DatabaseService
        db = DatabaseService()
        health = db.health_check()
        
        print("Database Service Status:")
        print(f"  Status: {health.get('status', 'unknown')}")
        print(f"  Path: {health.get('database_path', 'unknown')}")
        print(f"  Document Count: {health.get('document_count', 'unknown')}")
        print(f"  Writable: {health.get('writable', 'unknown')}")
        
        if health.get('error'):
            print(f"  Error: {health['error']}")
            
    except Exception as e:
        print(f"Database service error: {e}")

if __name__ == "__main__":
    check_database_status()