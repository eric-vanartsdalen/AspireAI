#!/usr/bin/env python3
"""
Quick build test script to determine the best configuration for your environment

This script helps identify which Docker configuration works best for your setup.
"""

import subprocess
import time
import os
import sys
from pathlib import Path

def run_command_with_timeout(command, description, timeout=300):
    """Run a command with timeout and measure execution time"""
    print(f"?? {description}...")
    start_time = time.time()
    
    try:
        result = subprocess.run(
            command, 
            shell=True, 
            check=True, 
            capture_output=True, 
            text=True,
            timeout=timeout
        )
        duration = time.time() - start_time
        print(f"? {description} completed in {duration:.1f} seconds")
        return True, duration, result.stdout
    except subprocess.TimeoutExpired:
        duration = time.time() - start_time
        print(f"? {description} timed out after {timeout} seconds")
        return False, duration, "TIMEOUT"
    except subprocess.CalledProcessError as e:
        duration = time.time() - start_time
        print(f"? {description} failed after {duration:.1f} seconds: {e}")
        return False, duration, e.stderr
    except Exception as e:
        duration = time.time() - start_time
        print(f"? {description} error after {duration:.1f} seconds: {e}")
        return False, duration, str(e)

def test_docker_builds():
    """Test different Docker build configurations"""
    print("?? Testing Docker Build Configurations")
    print("=" * 50)
    
    results = {}
    
    # Test 1: Lightweight build
    print("\n1. Testing Lightweight Build (Dockerfile.lightweight)")
    success, duration, output = run_command_with_timeout(
        "docker build -f Dockerfile.lightweight -t python-service-light .",
        "Building lightweight Docker image",
        timeout=300  # 5 minutes
    )
    results["lightweight"] = {"success": success, "duration": duration}
    
    # Test 2: Full build with optimizations
    print("\n2. Testing Optimized Full Build (Dockerfile)")
    success, duration, output = run_command_with_timeout(
        "DOCKER_BUILDKIT=1 docker build -t python-service-full .",
        "Building full Docker image with BuildKit",
        timeout=900  # 15 minutes
    )
    results["full"] = {"success": success, "duration": duration}
    
    # Test 3: Local virtual environment
    print("\n3. Testing Local Virtual Environment")
    if not Path(".venv").exists():
        success, duration, output = run_command_with_timeout(
            "python -m venv .venv",
            "Creating virtual environment",
            timeout=60
        )
        if success:
            if os.name == "nt":  # Windows
                pip_cmd = ".venv\\Scripts\\pip"
            else:  # Unix/MacOS
                pip_cmd = ".venv/bin/pip"
            
            success, duration, output = run_command_with_timeout(
                f"{pip_cmd} install fastapi uvicorn neo4j pypdf2 python-docx aiofiles pydantic python-multipart",
                "Installing basic dependencies in venv",
                timeout=300
            )
            results["venv"] = {"success": success, "duration": duration}
    else:
        print("? Virtual environment already exists")
        results["venv"] = {"success": True, "duration": 0}
    
    return results

def print_recommendations(results):
    """Print recommendations based on test results"""
    print("\n?? Build Configuration Recommendations")
    print("=" * 50)
    
    successful_builds = [(name, data) for name, data in results.items() if data["success"]]
    
    if not successful_builds:
        print("? No builds succeeded. Check Docker installation and network connectivity.")
        return
    
    # Sort by build time
    successful_builds.sort(key=lambda x: x[1]["duration"])
    
    print(f"\n?? Build Results (fastest to slowest):")
    for name, data in successful_builds:
        print(f"  {name:12} - {data['duration']:6.1f}s - {'? Success' if data['success'] else '? Failed'}")
    
    fastest = successful_builds[0]
    
    print(f"\n?? Recommended Configuration: {fastest[0].upper()}")
    
    if fastest[0] == "lightweight":
        print("""
? Use Lightweight Build:
   - Fast builds (~1-2 minutes)
   - Good for development
   - Basic document processing
   
?? Configuration:
   Set environment variable: USE_LIGHTWEIGHT_PYTHON=true
   Or use: docker build -f Dockerfile.lightweight
        """)
    elif fastest[0] == "venv":
        print("""
? Use Local Virtual Environment:
   - Fastest for development
   - Direct code changes without rebuilds
   - Full Python ecosystem access
   
?? Usage:
   python setup_dev_env.py
   .venv\\Scripts\\activate (Windows) or source .venv/bin/activate (Linux/Mac)
   uvicorn app.fastapi:app --host 0.0.0.0 --port 8000 --reload
        """)
    elif fastest[0] == "full":
        print("""
? Full Build Works:
   - Complete docling capabilities
   - Production-ready
   - Advanced document processing
   
?? Configuration:
   Enable BuildKit: DOCKER_BUILDKIT=1
   Use default Dockerfile
        """)
    
    print(f"\n?? Development Workflow Recommendation:")
    print(f"   1. Use {fastest[0]} for daily development")
    print(f"   2. Test with full build before production")
    print(f"   3. Use Docker volumes for persistent caching")

def main():
    """Main test function"""
    print("?? AspireAI Python Services - Build Configuration Test")
    print("This script will test different build approaches to find the best one for your environment.")
    print("Note: This may take several minutes...")
    
    # Check if we're in the right directory
    if not Path("requirements.txt").exists():
        print("? Please run this script from the AspireApp.PythonServices directory")
        sys.exit(1)
    
    # Run tests
    results = test_docker_builds()
    
    # Print recommendations
    print_recommendations(results)
    
    # Save results
    import json
    with open("build_test_results.json", "w") as f:
        json.dump(results, f, indent=2)
    
    print(f"\n?? Results saved to: build_test_results.json")
    print("You can use these results to configure your development environment optimally.")

if __name__ == "__main__":
    main()