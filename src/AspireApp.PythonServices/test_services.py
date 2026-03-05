"""
Test the document processing functionality
"""
import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from app.services.database_service import DatabaseService
from app.services.docling_service import DoclingService
from app.services.neo4j_service import Neo4jService

def test_services():
    """Test all services are working"""
    print("Testing services...")
    
    # Test database service
    try:
        db = DatabaseService()
        docs = db.get_all_documents()
        print(f"? Database service: Found {len(docs)} documents")
    except Exception as e:
        print(f"? Database service error: {e}")
    
    # Test docling service
    try:
        docling = DoclingService()
        print("? Docling service: Initialized successfully")
    except Exception as e:
        print(f"? Docling service error: {e}")
    
    # Test Neo4j service (connection might fail if not running)
    try:
        neo4j = Neo4jService()
        health = neo4j.health_check()
        if health:
            print("? Neo4j service: Connection healthy")
        else:
            print("??  Neo4j service: Connection failed (expected if Neo4j not running)")
        neo4j.close()
    except Exception as e:
        print(f"??  Neo4j service error: {e} (expected if Neo4j not running)")

if __name__ == "__main__":
    test_services()