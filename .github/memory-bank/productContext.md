# Product Context

Last reviewed: 2025-11-02

## 2025-11-02

### Why This Project Exists
AspireAI addresses the complexity of setting up local AI development environments. Traditional AI development requires managing multiple services, containers, and integrations, which can be daunting for developers learning AI concepts. This project provides a turnkey Aspire-orchestrated environment that "just works" for experimenting with AI features.

### Problems Solved
- **Service Orchestration Complexity**: Eliminates manual Docker Compose management by using .NET Aspire for declarative service wiring
- **AI Integration Barriers**: Provides pre-configured Ollama integration with local model serving
- **Document Processing Workflows**: Demonstrates end-to-end RAG pipelines from upload to AI response
- **Graph Database Learning**: Includes Neo4j for knowledge graph experiments
- **Development Reproducibility**: Ensures consistent local environments across different machines

### User Experience Goals
- **Zero-Config Startup**: Run `dotnet run --project src/AspireApp.AppHost` and have everything working
- **Clear Health Monitoring**: Aspire Dashboard shows service status at a glance
- **Intuitive AI Demos**: Web UI allows immediate experimentation with chat and document Q&A
- **Educational Value**: Code serves as examples for Aspire patterns, AI integration, and microservices
- **Extensibility**: Modular design allows adding new AI features or services

### Target Users
- .NET developers learning AI integration
- Teams evaluating Aspire for service orchestration
- Educators teaching AI concepts with practical examples
- Developers building AI-powered applications

### Business Value
- Accelerates AI learning and prototyping
- Demonstrates modern .NET ecosystem capabilities
- Provides reference architecture for AI applications
- Reduces time-to-experiment for AI features