#!/usr/bin/env python3
"""
Document Processing Demo Script

This script demonstrates how to use the document processing functionality.
It shows how to:
1. List documents
2. Process documents with docling
3. Search through processed content
4. Retrieve page content

Usage:
    python demo_processing.py
"""

import requests
import json
import time
from typing import List, Dict, Any

class DocumentProcessingClient:
    def __init__(self, base_url: str = "http://localhost:8000"):
        self.base_url = base_url.rstrip('/')
    
    def health_check(self) -> Dict[str, Any]:
        """Check if the service is healthy"""
        try:
            response = requests.get(f"{self.base_url}/health")
            response.raise_for_status()
            return response.json()
        except Exception as e:
            return {"error": str(e)}
    
    def list_documents(self) -> List[Dict[str, Any]]:
        """Get all documents"""
        response = requests.get(f"{self.base_url}/documents/")
        response.raise_for_status()
        return response.json()
    
    def list_unprocessed_documents(self) -> List[Dict[str, Any]]:
        """Get unprocessed documents"""
        response = requests.get(f"{self.base_url}/documents/unprocessed")
        response.raise_for_status()
        return response.json()
    
    def process_document(self, document_id: int) -> Dict[str, Any]:
        """Start processing a document"""
        response = requests.post(f"{self.base_url}/processing/process-document/{document_id}")
        response.raise_for_status()
        return response.json()
    
    def process_all_documents(self) -> Dict[str, Any]:
        """Start processing all unprocessed documents"""
        response = requests.post(f"{self.base_url}/processing/process-all")
        response.raise_for_status()
        return response.json()
    
    def get_processing_status(self, document_id: int) -> Dict[str, Any]:
        """Get processing status of a document"""
        response = requests.get(f"{self.base_url}/processing/status/{document_id}")
        response.raise_for_status()
        return response.json()
    
    def search_documents(self, query: str, limit: int = 10) -> Dict[str, Any]:
        """Search documents for content"""
        params = {"query": query, "limit": limit}
        response = requests.get(f"{self.base_url}/rag/search-documents", params=params)
        response.raise_for_status()
        return response.json()
    
    def get_document_context(self, document_id: int) -> Dict[str, Any]:
        """Get full context for a document"""
        response = requests.get(f"{self.base_url}/rag/document-context/{document_id}")
        response.raise_for_status()
        return response.json()
    
    def get_page_content(self, document_id: int, page_number: int) -> Dict[str, Any]:
        """Get specific page content"""
        response = requests.get(f"{self.base_url}/rag/page-content/{document_id}/{page_number}")
        response.raise_for_status()
        return response.json()

def main():
    """Main demo function"""
    print("?? AspireAI Document Processing Demo")
    print("=" * 50)
    
    client = DocumentProcessingClient()
    
    # Health check
    print("\n1. Checking service health...")
    health = client.health_check()
    print(f"   Health status: {json.dumps(health, indent=2)}")
    
    # List documents
    print("\n2. Listing all documents...")
    try:
        documents = client.list_documents()
        print(f"   Found {len(documents)} documents:")
        for doc in documents:
            print(f"   - ID: {doc['id']}, File: {doc['filename']}, Processed: {doc['processed']}")
    except Exception as e:
        print(f"   Error: {e}")
        return
    
    if not documents:
        print("   No documents found. Please upload some documents first.")
        return
    
    # List unprocessed documents
    print("\n3. Listing unprocessed documents...")
    try:
        unprocessed = client.list_unprocessed_documents()
        print(f"   Found {len(unprocessed)} unprocessed documents")
    except Exception as e:
        print(f"   Error: {e}")
    
    # Process documents if any are unprocessed
    if unprocessed:
        print("\n4. Starting document processing...")
        try:
            result = client.process_all_documents()
            print(f"   Processing started: {result}")
            
            # Wait a bit and check status
            print("\n5. Checking processing status...")
            time.sleep(2)
            
            for doc in unprocessed[:3]:  # Check first 3 documents
                try:
                    status = client.get_processing_status(doc['id'])
                    print(f"   Document {doc['id']}: {status['status']}")
                    if status.get('total_pages'):
                        print(f"     Pages: {status['total_pages']}")
                except Exception as e:
                    print(f"   Error checking status for document {doc['id']}: {e}")
                    
        except Exception as e:
            print(f"   Error: {e}")
    
    # Search functionality (if documents are processed)
    print("\n6. Testing search functionality...")
    try:
        # Try a general search
        search_results = client.search_documents("document", limit=5)
        print(f"   Search for 'document': {search_results['count']} results")
        
        if search_results['results']:
            first_result = search_results['results'][0]
            print(f"   First result: Document {first_result['document_id']}, Page {first_result['page_number']}")
            
            # Get full document context
            print(f"\n7. Getting document context for document {first_result['document_id']}...")
            try:
                context = client.get_document_context(first_result['document_id'])
                print(f"   Document: {context.get('filename', 'Unknown')}")
                print(f"   Pages: {len(context.get('pages', []))}")
            except Exception as e:
                print(f"   Error getting context: {e}")
        
    except Exception as e:
        print(f"   Search error (expected if no documents processed yet): {e}")
    
    print("\n? Demo completed!")
    print("\nNext steps:")
    print("- Upload documents through the Blazor frontend")
    print("- Wait for processing to complete")
    print("- Use the search functionality to find content")
    print("- Access individual pages and full document context")

if __name__ == "__main__":
    main()