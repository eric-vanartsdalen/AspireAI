# AspireAI Python Services Build Configuration

This document explains how to handle build issues and choose the right configuration for your environment.

## ?? Quick Fix for Current Issue

**Problem**: `ModuleNotFoundError: No module named 'docling'` when using lightweight build

**Solution**: The service now automatically detects available dependencies and uses fallback processors.

### ? Immediate Fix Applied

1. **Service Factory**: Automatically selects the right document processor
2. **Fallback Processing**: Works with PyPDF2 and python-docx when docling unavailable
3. **Smart Imports**: Graceful handling of missing dependencies
4. **Service Info**: New endpoint to check capabilities: `/processing/service-info`

### ?? Verification Steps

```bash
# 1. Build and start the service
dotnet run --project src/AspireApp.AppHost

# 2. Wait for containers to start, then test
cd src/AspireApp.PythonServices
python verify_service.py

# 3. Check service capabilities
curl http://localhost:8000/docs
```

## Build Issues Resolution

### Problem: Long Build Times / Timeouts

The Python service includes heavy ML dependencies (docling, CUDA packages) that can cause:
- Build timeouts (5+ minutes)
- Docker context canceled errors
- Memory issues during pip install

### Solutions

#### Option 1: Use Lightweight Build (Recommended for Development)

Set the environment variable to use the lightweight dockerfile:

**In your environment variables or appsettings:**
```json
{
  "USE_LIGHTWEIGHT_PYTHON": "true"
}
```

This uses `Dockerfile.lightweight` which:
- ? Builds in ~1-2 minutes
- ? Includes PDF/DOCX processing (PyPDF2, python-docx)
- ? Supports all API endpoints
- ? Automatic fallback when docling unavailable
- ?? No advanced ML-based layout analysis

#### Option 2: Optimize Full Build

To use full docling capabilities with optimized build:

1. **Increase Docker timeouts:**
```bash
# Set Docker build timeout
export DOCKER_CLIENT_TIMEOUT=1200
export COMPOSE_HTTP_TIMEOUT=1200
```

2. **Use the optimized Dockerfile** (already configured):
- Staged builds for better caching
- Increased pip timeouts
- Split dependency installation

3. **Enable BuildKit for better caching:**
```bash
export DOCKER_BUILDKIT=1
```

#### Option 3: Local Development

For fastest development iteration:

```bash
cd src/AspireApp.PythonServices
python setup_dev_env.py
.venv\Scripts\activate  # Windows
source .venv/bin/activate  # Linux/Mac
uvicorn app.fastapi:app --host 0.0.0.0 --port 8000 --reload
```

## Configuration Options

### Dockerfile Selection

| Dockerfile | Build Time | Capabilities | Use Case | Status |
|------------|------------|--------------|----------|---------|
| `Dockerfile` | 5-10 min | Full docling + ML | Production | ?? Can timeout |
| `Dockerfile.lightweight` | 1-2 min | PDF/DOCX processing | Development | ? Reliable |
| Local .venv | 2 min setup | All (with local install) | Rapid dev | ? Fastest |

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `USE_LIGHTWEIGHT_PYTHON` | `false` | Use lightweight build |
| `DOCKER_BUILDKIT` | `1` | Enable BuildKit caching |
| `PIP_DEFAULT_TIMEOUT` | `300` | pip install timeout |

### Document Processing Capabilities

#### Full Docling (Dockerfile)
- ? Advanced layout analysis
- ? Table extraction
- ? Image processing
- ? Multiple document formats
- ? High-quality text extraction

#### Lightweight (Dockerfile.lightweight + Fallback Service)
- ? PDF text extraction (PyPDF2)
- ? DOCX processing (python-docx)
- ? Basic text files (any encoding)
- ? All API endpoints work
- ? Automatic service detection
- ? Graceful fallback handling
- ?? No advanced layout analysis
- ?? Limited table extraction

## Service Architecture

### ?? Smart Service Detection

The service now includes a **Service Factory** that automatically detects available dependencies:

```python
# Automatic service selection
try:
    from .docling_service import DoclingService  # Full docling
except ImportError:
    from .docling_service_fallback import DoclingService  # Fallback
```

### ?? Service Info Endpoint

**GET** `/processing/service-info` returns:

```json
{
  "docling_available": false,
  "service_type": "fallback",
  "capabilities": {
    "pdf_processing": true,
    "docx_processing": true,
    "advanced_layout": false,
    "table_extraction": false,
    "image_processing": false
  }
}
```

### ?? Enhanced Health Check

**GET** `/health` now includes service capabilities:

```json
{
  "status": "healthy",
  "service_info": {
    "service_type": "fallback",
    "capabilities": { ... }
  },
  "data_path_exists": true,
  "database_path_exists": true
}
```

## Troubleshooting

### ? FIXED: Build Fails with "ModuleNotFoundError: No module named 'docling'"

**Root Cause**: Lightweight build doesn't include docling, but service tried to import it anyway.

**Solution Applied**:
1. Service factory automatically detects available dependencies
2. Fallback service provides same API with basic document processing
3. Graceful error handling for missing dependencies

### Build Fails with "context canceled"

```bash
# Increase timeouts
export DOCKER_CLIENT_TIMEOUT=1200
export COMPOSE_HTTP_TIMEOUT=1200

# Use lightweight build
# Set USE_LIGHTWEIGHT_PYTHON=true in appsettings
```

### Build Fails with Memory Issues

```bash
# Increase Docker memory limit
# Docker Desktop: Settings > Resources > Memory > 4GB+

# Or use lightweight build
```

### Dependencies Not Installing

```bash
# Clear Docker cache
docker builder prune -a

# Rebuild from scratch
docker-compose build --no-cache python-service
```

### Local Development Issues

```bash
# Reset virtual environment
rm -rf .venv
python setup_dev_env.py

# Install minimal dependencies only
pip install fastapi uvicorn neo4j pypdf2 python-docx
```

### Service Startup Issues

```bash
# Check service status
python verify_service.py

# Check logs
docker logs python-service

# Test individual endpoints
curl http://localhost:8000/health
curl http://localhost:8000/processing/service-info
```

## Recommendations

### For Development (Current Setup)
1. ? **Use lightweight build** with `USE_LIGHTWEIGHT_PYTHON=true`
2. ? **Service auto-detection** handles missing dependencies
3. ? **Fallback processing** provides full API compatibility
4. ? **Fast builds** (~1-2 minutes)

### For Production
1. **Use full Dockerfile** with proper timeouts
2. **Pre-built images** in CI/CD pipeline
3. **Resource monitoring** for document processing

### For CI/CD
1. **Cache Docker layers** between builds
2. **Split builds** (dependencies vs application)
3. **Parallel builds** for different configurations

## Example Configurations

### appsettings.Development.json (Current - Working)
```json
{
  "USE_LIGHTWEIGHT_PYTHON": "true",
  "USE_LIGHTWEIGHT_NEO4J": "true"
}
```

### appsettings.Production.json
```json
{
  "USE_LIGHTWEIGHT_PYTHON": "false",
  "USE_LIGHTWEIGHT_NEO4J": "false"
}
```

## Current Status

? **FIXED**: Lightweight build now works correctly
? **TESTED**: Service factory handles missing dependencies
? **VERIFIED**: All API endpoints functional with fallback processing
? **DOCUMENTED**: Service capabilities clearly indicated

**The service is now fully functional with the lightweight build configuration!**