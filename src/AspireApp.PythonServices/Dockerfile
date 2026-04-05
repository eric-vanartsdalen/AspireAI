# Use the official Python image from the Docker Hub
FROM python:3.12.7-slim AS base

# Set environment variables for Python optimization
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_DEFAULT_TIMEOUT=1000 \
    PIP_RETRIES=5

# Install system dependencies and build tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    git \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Dependencies stage - this layer will be cached unless requirements.txt changes
FROM base AS dependencies

# Set the working directory
WORKDIR /app

# Copy only requirements first (for better caching)
COPY requirements.txt .

# Create virtual environment and install dependencies with optimizations
RUN python -m venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"

# Install dependencies with increased timeout and retries
# Split large installs to avoid timeout issues
RUN pip install --upgrade pip setuptools wheel

# Install core dependencies first (smaller, more stable)
RUN pip install --timeout=1000 --retries=5 \
    anyio fastapi uvicorn pydantic aiofiles python-multipart python-json-logger

# Install Neo4j driver
RUN pip install --timeout=1000 --retries=5 neo4j

# Install document processing dependencies (these are large)
RUN pip install --timeout=1000 --retries=5 --no-deps \
    pypdf2

# Install docling and related packages (largest dependencies)
# Use --no-deps first, then install dependencies separately to control the process
RUN pip install --timeout=1000 --retries=5 \
    docling-core

RUN pip install --timeout=1000 --retries=5 \
    docling-ibm-models

RUN pip install --timeout=1000 --retries=5 \
    docling

# Production stage
FROM base AS production

# Create non-root user and install gosu for reliable privilege dropping
RUN adduser --disabled-password --gecos '' --uid 1000 appuser && \
    apt-get update && apt-get install -y --no-install-recommends gosu && \
    rm -rf /var/lib/apt/lists/*

# Copy the virtual environment from dependencies stage
COPY --from=dependencies /opt/venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"

# Set the working directory
WORKDIR /app

# Pre-create directories and give appuser ownership; bind mounts will
# overlay these at runtime, but the entrypoint re-applies ownership/chmod.
RUN mkdir -p /app/database /app/docs-database /app/data/uploads /app/data/processed/documents && \
    chown -R appuser:appuser /app

# Copy the application code
COPY --chown=appuser:appuser . .

# Copy entrypoint to root so it is not shadowed by WORKDIR/app files
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Expose the port the app runs on
EXPOSE 8000

# entrypoint.sh runs as root, fixes bind-mount permissions, then execs as appuser
ENTRYPOINT ["/entrypoint.sh"]
CMD ["/opt/venv/bin/uvicorn", "app.fastapi:app", "--host", "0.0.0.0", "--port", "8000"]
