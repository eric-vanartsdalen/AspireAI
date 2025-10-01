#!/usr/bin/env python3
"""
Quick verification script to test the Python service after startup

This script verifies that the service is working correctly with either
full docling or fallback processing, and tests database connectivity.
"""

import requests
import time
import json
from typing import Dict, Any

def wait_for_service(url: str = "http://localhost:8000", timeout: int = 60) -> bool:
    """Wait for the service to be available"""
    print(f"?? Waiting for service at {url}...")
    
    start_time = time.time()
    while time.time() - start_time < timeout:
        try:
            response = requests.get(f"{url}/health", timeout=5)
            if response.status_code == 200:
                print(f"? Service is available!")
                return True
        except requests.RequestException:
            pass
        
        time.sleep(2)
        print(".", end="", flush=True)
    
    print(f"\n? Service not available after {timeout} seconds")
    return False

def test_database_health(url: str = "http://localhost:8000") -> bool:
    """Test database health specifically"""
    print("\n?? Testing Database Health")
    print("-" * 30)
    
    try:
        response = requests.get(f"{url}/documents/health/database")
        if response.status_code == 200:
            health_data = response.json()
            print("? Database health check passed")
            print(f"   Database path: {health_data.get('database_path', 'unknown')}")
            print(f"   Document count: {health_data.get('document_count', 0)}")
            print(f"   Writable: {health_data.get('writable', False)}")
            return True
        else:
            print(f"? Database health check failed: {response.status_code}")
            try:
                error_data = response.json()
                print(f"   Error: {error_data}")
            except:
                print(f"   Response: {response.text}")
            return False
    except Exception as e:
        print(f"? Database health check error: {e}")
        return False

def test_service_capabilities(url: str = "http://localhost:8000") -> Dict[str, Any]:
    """Test service capabilities and health"""
    print("\n?? Testing Service Capabilities")
    print("=" * 40)
    
    results = {}
    
    # Test health endpoint
    try:
        response = requests.get(f"{url}/health")
        if response.status_code == 200:
            health_data = response.json()
            print("? Health check passed")
            print(f"   Status: {health_data.get('status', 'unknown')}")
            
            service_info = health_data.get('service_info', {})
            if service_info:
                print(f"   Service type: {service_info.get('service_type', 'unknown')}")
                print(f"   Docling available: {service_info.get('docling_available', False)}")
                
                capabilities = service_info.get('capabilities', {})
                for cap, available in capabilities.items():
                    status = "?" if available else "?"
                    print(f"   {cap}: {status}")
            
            results['health'] = True
        else:
            print(f"? Health check failed: {response.status_code}")
            results['health'] = False
    except Exception as e:
        print(f"? Health check error: {e}")
        results['health'] = False
    
    # Test database health
    results['database'] = test_database_health(url)
    
    # Test service info endpoint
    try:
        response = requests.get(f"{url}/processing/service-info")
        if response.status_code == 200:
            service_data = response.json()
            print("? Service info endpoint working")
            results['service_info'] = True
        else:
            print(f"? Service info failed: {response.status_code}")
            results['service_info'] = False
    except Exception as e:
        print(f"? Service info error: {e}")
        results['service_info'] = False
    
    # Test documents endpoint
    try:
        response = requests.get(f"{url}/documents/")
        if response.status_code == 200:
            docs = response.json()
            print(f"? Documents endpoint working ({len(docs)} documents)")
            results['documents'] = True
        else:
            print(f"? Documents endpoint failed: {response.status_code}")
            try:
                error_data = response.json()
                print(f"   Error: {error_data}")
            except:
                print(f"   Response: {response.text}")
            results['documents'] = False
    except Exception as e:
        print(f"? Documents endpoint error: {e}")
        results['documents'] = False
    
    # Test RAG health
    try:
        response = requests.get(f"{url}/rag/health")
        if response.status_code == 200:
            rag_health = response.json()
            print(f"? RAG health endpoint working")
            overall_status = rag_health.get('overall', 'unknown')
            print(f"   Overall RAG status: {overall_status}")
            results['rag'] = True
        else:
            print(f"? RAG health failed: {response.status_code}")
            results['rag'] = False
    except Exception as e:
        print(f"? RAG health error: {e}")
        results['rag'] = False
    
    return results

def print_recommendations(results: Dict[str, Any]):
    """Print recommendations based on test results"""
    print("\n?? Service Verification Results")
    print("=" * 40)
    
    passed = sum(1 for v in results.values() if v)
    total = len(results)
    
    print(f"Tests passed: {passed}/{total}")
    
    if passed == total:
        print("?? All tests passed! Service is working correctly.")
        print("\n? Ready for document processing:")
        print("   1. Upload documents through Blazor frontend")
        print("   2. Process documents via /processing/process-all")
        print("   3. Search content via /rag/search-documents")
    else:
        print("?? Some tests failed. Check the issues below:")
        
        if not results.get('health', False):
            print("   ?? Health check failed - check service startup")
        if not results.get('database', False):
            print("   ?? Database failed - run database diagnostic")
            print("      Try: python diagnose_database.py")
            print("      Or: python fix_database.py")
        if not results.get('rag', False):
            print("   ?? RAG services failed - check Neo4j connection")
        if not results.get('documents', False):
            print("   ?? Documents API failed - likely database issue")

def main():
    """Main verification function"""
    print("?? AspireAI Python Service Verification")
    print("This script verifies that the Python service is working correctly.")
    print()
    
    # Wait for service to be available
    if not wait_for_service():
        print("? Service verification failed - service not available")
        print("\n?? Troubleshooting steps:")
        print("   1. Check if containers are running: docker ps")
        print("   2. Check service logs: docker logs python-service")
        print("   3. Verify Aspire is running: dotnet run --project src/AspireApp.AppHost")
        return
    
    # Test service capabilities
    results = test_service_capabilities()
    
    # Print recommendations
    print_recommendations(results)
    
    print(f"\n?? Next steps:")
    print("   - View API docs: http://localhost:8000/docs")
    print("   - Check health: http://localhost:8000/health")
    print("   - Database health: http://localhost:8000/documents/health/database")
    print("   - Service info: http://localhost:8000/processing/service-info")

if __name__ == "__main__":
    main()