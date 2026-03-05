#!/usr/bin/env python3
"""
Development environment setup script for faster local development

This script helps set up a local virtual environment for development
without needing to rebuild Docker containers constantly.

Usage:
    python setup_dev_env.py
"""

import os
import subprocess
import sys
from pathlib import Path

def run_command(command, description):
    """Run a command and handle errors"""
    print(f"?? {description}...")
    try:
        result = subprocess.run(command, shell=True, check=True, capture_output=True, text=True)
        print(f"? {description} completed successfully")
        return result.stdout
    except subprocess.CalledProcessError as e:
        print(f"? {description} failed: {e}")
        print(f"Error output: {e.stderr}")
        return None

def setup_virtual_environment():
    """Set up a local virtual environment for development"""
    
    # Check if we're in the right directory
    if not Path("requirements.txt").exists():
        print("? requirements.txt not found. Please run this script from the PythonServices directory.")
        return False
    
    # Create virtual environment
    if not Path(".venv").exists():
        run_command("python -m venv .venv", "Creating virtual environment")
    else:
        print("? Virtual environment already exists")
    
    # Activate and install requirements
    if os.name == "nt":  # Windows
        activate_cmd = ".venv\\Scripts\\activate && "
        pip_cmd = ".venv\\Scripts\\pip"
    else:  # Unix/MacOS
        activate_cmd = "source .venv/bin/activate && "
        pip_cmd = ".venv/bin/pip"
    
    # Upgrade pip
    run_command(f"{pip_cmd} install --upgrade pip", "Upgrading pip")
    
    # Install requirements
    run_command(f"{pip_cmd} install -r requirements.txt", "Installing Python dependencies")
    
    # Install development dependencies
    dev_requirements = [
        "pytest",
        "pytest-asyncio", 
        "httpx",  # For testing FastAPI
        "black",  # Code formatting
        "flake8", # Linting
        "mypy",   # Type checking
    ]
    
    for req in dev_requirements:
        run_command(f"{pip_cmd} install {req}", f"Installing {req}")
    
    print("\n?? Development environment setup complete!")
    print("\nTo activate the environment:")
    if os.name == "nt":
        print("  .venv\\Scripts\\activate")
    else:
        print("  source .venv/bin/activate")
    
    print("\nTo run the FastAPI service locally:")
    print("  uvicorn app.fastapi:app --host 0.0.0.0 --port 8000 --reload")
    
    return True

def create_dev_compose():
    """Create a docker-compose for development with volume caching"""
    compose_content = """version: '3.8'

services:
  python-service-dev:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "8000:8000"
    volumes:
      - .:/app
      - ../../data:/app/data
      - ../../database:/app/database
      - python-pip-cache:/root/.cache/pip
      - python-venv-cache:/opt/venv
    environment:
      - NEO4J_URI=bolt://neo4j:7687
      - NEO4J_USER=neo4j
      - NEO4J_PASSWORD=neo4j@secret
      - PYTHONDONTWRITEBYTECODE=1
      - PYTHONUNBUFFERED=1
    command: uvicorn app.fastapi:app --host 0.0.0.0 --port 8000 --reload
    depends_on:
      - neo4j

  neo4j:
    image: neo4j:latest
    ports:
      - "7474:7474"
      - "7687:7687"
    environment:
      - NEO4J_AUTH=neo4j/neo4j@secret
    volumes:
      - neo4j-data:/data
      - neo4j-logs:/logs

volumes:
  python-pip-cache:
  python-venv-cache:
  neo4j-data:
  neo4j-logs:
"""
    
    with open("docker-compose.dev.yml", "w") as f:
        f.write(compose_content)
    
    print("? Created docker-compose.dev.yml for development")
    print("   Use: docker-compose -f docker-compose.dev.yml up")

if __name__ == "__main__":
    print("?? AspireAI Python Services - Development Environment Setup")
    print("=" * 60)
    
    if setup_virtual_environment():
        create_dev_compose()
        
        print("\n?? Development Options:")
        print("1. Local development with .venv (fastest for code changes)")
        print("2. Docker development with cached volumes (docker-compose.dev.yml)")
        print("3. Full Aspire orchestration (production-like)")
        
        print("\n?? Tips for faster development:")
        print("- Use option 1 for rapid code iteration")
        print("- Use option 2 when you need the full containerized environment")
        print("- The Dockerfile is optimized for layer caching")
        print("- Docker volumes persist pip cache and virtual environment")
    else:
        print("? Setup failed. Please check the errors above.")
        sys.exit(1)