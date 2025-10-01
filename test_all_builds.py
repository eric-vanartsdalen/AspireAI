#!/usr/bin/env python3
"""
Comprehensive build test script for AspireAI services

Tests both Python and Neo4j service build optimizations to find the best configuration.
"""

import subprocess
import time
import os
import sys
import json
from pathlib import Path

def run_command_with_timeout(command, description, timeout=600, cwd=None):
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
            timeout=timeout,
            cwd=cwd
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
        print(f"? {description} failed after {duration:.1f} seconds")
        return False, duration, e.stderr
    except Exception as e:
        duration = time.time() - start_time
        print(f"? {description} error after {duration:.1f} seconds: {e}")
        return False, duration, str(e)

def test_python_builds():
    """Test Python service build configurations"""
    print("\n?? Testing Python Service Build Configurations")
    print("=" * 60)
    
    results = {}
    python_dir = Path("src/AspireApp.PythonServices")
    
    if not python_dir.exists():
        print("? Python services directory not found")
        return results
    
    # Test 1: Lightweight Python build
    print("\n1. Testing Lightweight Python Build")
    success, duration, output = run_command_with_timeout(
        "docker build -f Dockerfile.lightweight -t python-service-light .",
        "Building lightweight Python image",
        timeout=300,
        cwd=python_dir
    )
    results["python_lightweight"] = {"success": success, "duration": duration}
    
    # Test 2: Optimized Python build (with BuildKit)
    print("\n2. Testing Optimized Python Build")
    env = os.environ.copy()
    env["DOCKER_BUILDKIT"] = "1"
    
    success, duration, output = run_command_with_timeout(
        "docker build -t python-service-optimized .",
        "Building optimized Python image with BuildKit",
        timeout=900,
        cwd=python_dir
    )
    results["python_optimized"] = {"success": success, "duration": duration}
    
    return results

def test_neo4j_builds():
    """Test Neo4j service build configurations"""
    print("\n?? Testing Neo4j Service Build Configurations")
    print("=" * 60)
    
    results = {}
    neo4j_dir = Path("src/AspireApp.Neo4jService")
    
    if not neo4j_dir.exists():
        print("? Neo4j services directory not found")
        return results
    
    # Test 1: Lightweight Neo4j build
    print("\n1. Testing Lightweight Neo4j Build")
    success, duration, output = run_command_with_timeout(
        "docker build -f Dockerfile.lightweight -t neo4j-light .",
        "Building lightweight Neo4j image",
        timeout=180,
        cwd=neo4j_dir
    )
    results["neo4j_lightweight"] = {"success": success, "duration": duration}
    
    # Test 2: Optimized Neo4j build
    print("\n2. Testing Optimized Neo4j Build")
    success, duration, output = run_command_with_timeout(
        "DOCKER_BUILDKIT=1 docker build -t neo4j-optimized .",
        "Building optimized Neo4j image with plugins",
        timeout=600,
        cwd=neo4j_dir
    )
    results["neo4j_optimized"] = {"success": success, "duration": duration}
    
    return results

def test_aspire_build():
    """Test full Aspire application build"""
    print("\n??? Testing Full Aspire Application Build")
    print("=" * 60)
    
    results = {}
    
    # Test .NET build
    print("\n1. Testing .NET Solution Build")
    success, duration, output = run_command_with_timeout(
        "dotnet build",
        "Building .NET solution",
        timeout=300
    )
    results["dotnet_build"] = {"success": success, "duration": duration}
    
    # Test with lightweight configuration
    print("\n2. Testing Aspire with Lightweight Configuration")
    # This would require running the actual Aspire host, which is complex in a script
    # For now, we'll just validate the configuration
    config_files = [
        "src/AspireApp.AppHost/appsettings.json",
        "src/AspireApp.AppHost/appsettings.Development.json"
    ]
    
    config_valid = True
    for config_file in config_files:
        if not Path(config_file).exists():
            print(f"? Configuration file missing: {config_file}")
            config_valid = False
        else:
            try:
                with open(config_file) as f:
                    json.load(f)
                print(f"? Configuration file valid: {config_file}")
            except json.JSONDecodeError:
                print(f"? Invalid JSON in: {config_file}")
                config_valid = False
    
    results["aspire_config"] = {"success": config_valid, "duration": 0}
    
    return results

def test_service_startup():
    """Test individual service startup times"""
    print("\n?? Testing Service Startup Performance")
    print("=" * 60)
    
    results = {}
    
    # Test Neo4j lightweight startup
    print("\n1. Testing Neo4j Lightweight Startup")
    success, duration, output = run_command_with_timeout(
        "docker run -d --name neo4j-test -p 7474:7474 -p 7687:7687 neo4j-light",
        "Starting Neo4j lightweight container",
        timeout=60
    )
    
    if success:
        # Wait for Neo4j to be ready
        time.sleep(15)
        ready_success, ready_duration, ready_output = run_command_with_timeout(
            "curl -f http://localhost:7474/ -s -o /dev/null",
            "Waiting for Neo4j to be ready",
            timeout=30
        )
        
        if ready_success:
            total_startup = duration + ready_duration
            print(f"? Neo4j ready in {total_startup:.1f} seconds total")
            results["neo4j_startup"] = {"success": True, "duration": total_startup}
        else:
            results["neo4j_startup"] = {"success": False, "duration": duration}
        
        # Cleanup
        subprocess.run("docker stop neo4j-test && docker rm neo4j-test", shell=True, capture_output=True)
    else:
        results["neo4j_startup"] = {"success": False, "duration": duration}
    
    return results

def print_comprehensive_report(python_results, neo4j_results, aspire_results, startup_results):
    """Print comprehensive build performance report"""
    print("\n?? Comprehensive Build Performance Report")
    print("=" * 80)
    
    all_results = {
        **python_results,
        **neo4j_results, 
        **aspire_results,
        **startup_results
    }
    
    # Successful builds
    successful = [(name, data) for name, data in all_results.items() if data["success"]]
    failed = [(name, data) for name, data in all_results.items() if not data["success"]]
    
    if successful:
        print(f"\n? Successful Builds ({len(successful)}):")
        successful.sort(key=lambda x: x[1]["duration"])
        for name, data in successful:
            print(f"  {name:20} - {data['duration']:6.1f}s")
    
    if failed:
        print(f"\n? Failed Builds ({len(failed)}):")
        for name, data in failed:
            print(f"  {name:20} - Failed after {data['duration']:6.1f}s")
    
    # Recommendations
    print(f"\n?? Performance Recommendations:")
    
    # Python recommendations
    python_builds = {k: v for k, v in all_results.items() if k.startswith("python_")}
    if python_builds:
        fastest_python = min(
            [(k, v) for k, v in python_builds.items() if v["success"]], 
            key=lambda x: x[1]["duration"],
            default=None
        )
        if fastest_python:
            print(f"  ?? Python: Use {fastest_python[0]} ({fastest_python[1]['duration']:.1f}s)")
    
    # Neo4j recommendations
    neo4j_builds = {k: v for k, v in all_results.items() if k.startswith("neo4j_")}
    if neo4j_builds:
        fastest_neo4j = min(
            [(k, v) for k, v in neo4j_builds.items() if v["success"]], 
            key=lambda x: x[1]["duration"],
            default=None
        )
        if fastest_neo4j:
            print(f"  ?? Neo4j: Use {fastest_neo4j[0]} ({fastest_neo4j[1]['duration']:.1f}s)")
    
    # Configuration recommendations
    print(f"\n?? Recommended Configuration:")
    print(f"  Development: USE_LIGHTWEIGHT_PYTHON=true, USE_LIGHTWEIGHT_NEO4J=true")
    print(f"  Production:  USE_LIGHTWEIGHT_PYTHON=false, USE_LIGHTWEIGHT_NEO4J=false")
    print(f"  Always:      DOCKER_BUILDKIT=1")
    
    # Total time estimates
    if successful:
        total_dev_time = sum(data["duration"] for name, data in successful if "lightweight" in name)
        total_prod_time = sum(data["duration"] for name, data in successful if "optimized" in name)
        
        print(f"\n?? Estimated Total Build Times:")
        print(f"  Development (lightweight): ~{total_dev_time:.0f} seconds")
        print(f"  Production (optimized):    ~{total_prod_time:.0f} seconds")

def main():
    """Main test function"""
    print("?? AspireAI Comprehensive Build Performance Test")
    print("This script tests all build configurations to optimize your development workflow.")
    print("Note: This may take 10-15 minutes to complete all tests...")
    print()
    
    # Ensure we're in the right directory
    if not Path("AspireApp.sln").exists():
        print("? Please run this script from the AspireAI solution root directory")
        sys.exit(1)
    
    # Run all tests
    start_time = time.time()
    
    python_results = test_python_builds()
    neo4j_results = test_neo4j_builds()
    aspire_results = test_aspire_build()
    startup_results = test_service_startup()
    
    total_time = time.time() - start_time
    
    # Generate comprehensive report
    print_comprehensive_report(python_results, neo4j_results, aspire_results, startup_results)
    
    # Save results
    all_results = {
        "python": python_results,
        "neo4j": neo4j_results,
        "aspire": aspire_results,
        "startup": startup_results,
        "test_duration": total_time,
        "timestamp": time.time()
    }
    
    with open("build_performance_report.json", "w") as f:
        json.dump(all_results, f, indent=2)
    
    print(f"\n?? Complete results saved to: build_performance_report.json")
    print(f"?? Total test time: {total_time:.1f} seconds")
    print("\nUse these results to optimize your development environment!")

if __name__ == "__main__":
    main()