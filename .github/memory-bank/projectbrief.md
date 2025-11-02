# Project Brief

Last reviewed: 2025-11-02

## 2025-11-02

### Overview
AspireAI is a .NET Aspire AppHost orchestration platform for learning and demonstrating AI-integrated applications. It provides a reproducible local development environment for experimenting with RAG (Retrieval-Augmented Generation) document flows, AI model integrations, and graph database operations.

### Core Objectives
- Demonstrate .NET Aspire orchestration of multi-service applications
- Provide hands-on experience with AI model integration (Ollama)
- Showcase document ingestion and Graph-RAG workflows
- Maintain modular, configurable services for easy extension
- Ensure reproducible local development via containerized services

### Key Components
- **Blazor Web UI**: Chat interface, document upload, AI demos
- **Python FastAPI Backend**: Document processing, Graph-RAG experiments
- **Ollama Integration**: Local AI model serving
- **Neo4j Graph Database**: Knowledge graph storage
- **Aspire AppHost**: Service orchestration and health monitoring

### Success Criteria
- Services start reliably via `dotnet run --project src/AspireApp.AppHost`
- Dashboard provides clear visibility into service health
- Document upload and AI chat workflows function end-to-end
- Code follows .NET best practices and is well-documented
- Repository serves as a reference for Aspire-based AI applications

### Constraints
- Local development focus (no cloud deployment included)
- Work-in-progress status for Python and Neo4j integrations
- Learning/demo oriented rather than production-ready