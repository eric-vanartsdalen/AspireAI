using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Threading;
using System.Threading.Tasks;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        // ASPIRE LOCAL SETUP
        var builder = DistributedApplication.CreateBuilder(args);

        // Config with .NET Aspire
        var aiChatModel = builder.AddParameterFromConfiguration("AI-Chat-Model", "AI-Chat-Model");
        var aiEmbeddings = builder.AddParameterFromConfiguration("AI-Embedding-Model", "AI-Embedding-Model");
        var aiEndpoint = builder.AddParameterFromConfiguration("AI-Endpoint", "AI-Endpoint");

        // API Service
        var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
            .WithHttpHealthCheck("/health");

        // SETUP OLLAMA & MODEL CONTAINERS
        var chatModelName = builder.Configuration["AI-Chat-Model"] ?? "phi4-mini:latest";
        var embeddingModelName = builder.Configuration["AI-Embedding-Model"] ?? "nomic-embed-text:latest";
        var ollama = builder.AddOllama("ollama")
            .WithAnnotation(new ContainerImageAnnotation { 
				Image = "ollama/ollama", 
				Tag = "latest"
			})
			.WithDataVolume()
			.WithGPUSupport();
            //.WithContainerRuntimeArgs("--gpus", "all");
        var appmodel = ollama.AddModel("chat", chatModelName);
        var embeddingmodel = ollama.AddModel("embedding", embeddingModelName);

		// Add a NEO4J container for graph database with caching optimizations
		var neo4jUser = builder.AddParameter("neo4j-user", "neo4j");
		var neo4jPass = builder.AddParameter("neo4j-pass", "neo4j@secret", secret: true);

		// Retrieve parameter values asynchronously to avoid using obsolete .Value
		var neo4jUserValue = await neo4jUser.Resource.GetValueAsync(CancellationToken.None);
		var neo4jPassValue = await neo4jPass.Resource.GetValueAsync(CancellationToken.None);

		// Configure Neo4j build options
		var useLightweightBuild = builder.Configuration["USE_LIGHTWEIGHT_PYTHON"] ?? "false";
		var useLightweightNeo4j = builder.Configuration["USE_LIGHTWEIGHT_NEO4J"] ?? "false";
		var neo4jDockerfile = useLightweightNeo4j.ToLower() == "true" ? "Dockerfile.lightweight" : "Dockerfile";

        var neo4jDb = builder.AddDockerfile("graph-db", "../../src/AspireApp.Neo4jService/", neo4jDockerfile)
			.WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")  // Neo4j browser interface
			.WithEndpoint(port: 7687, targetPort: 7687, name: "bolt")      // Neo4j bolt protocol
			.WithBindMount("../../database/neo4j/data", "/data")           // Persistent data
			.WithBindMount("../../database/neo4j/logs", "/logs")           // Logs persistence
			.WithBindMount("../../database/neo4j/plugins", "/plugins")     // Plugin persistence
			.WithBindMount("../../database/neo4j/conf", "/conf")           // Configuration persistence
			.WithBindMount("../../database/neo4j/import", "/import")       // Import persistence
			.WithBindMount("../../database/neo4j/metrics", "/metrics")     // Metrics persistence (optional)
			.WithBindMount("../../database/neo4j/backup", "/backup")       // Backup directory (optional)															   
			.WithEnvironment("NEO4J_AUTH", $"{neo4jUserValue}/{neo4jPassValue}")
			.WithEnvironment("NEO4J_ACCEPT_LICENSE_AGREEMENT", "yes")
			.WithEnvironment("DOCKER_BUILDKIT", "1")                      // Enable BuildKit for Neo4j too
			.WithHttpHealthCheck("/");

		// Setup Python services environment with optimized caching and build settings
		var pythonDockerfile = useLightweightBuild.ToLower() == "true" ? "Dockerfile.lightweight" : "Dockerfile";

		var pythonServices = builder
			.AddDockerfile("python-service", "../../src/AspireApp.PythonServices/", pythonDockerfile)
			.WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
			.WithBindMount("../../data", "/app/data")
			.WithBindMount("../../database", "/app/database")					  // Keep host access for debugging/backup
			.WithVolume("python-pip-cache", "/root/.cache/pip")					  // Persist pip cache
			.WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
			.WithEnvironment("NEO4J_USER", neo4jUser.Resource)
			.WithEnvironment("NEO4J_PASSWORD", neo4jPass.Resource)
			.WithEnvironment("PIP_CACHE_DIR", "/root/.cache/pip")				   // Use persistent pip cache
			.WithEnvironment("DOCKER_BUILDKIT", "1")							   // Enable BuildKit for better caching
			.WithEnvironment("ASPIRE_DB_PATH", "/app/database/data-resources.db")  // Use host-mounted path by default
			.WithHttpHealthCheck("/health")
			.WaitFor(neo4jDb);  // Ensure Neo4j starts before Python service

		// SETUP CONTAINER LightRAG service
		// see: https://github.com/hkuds/LightRAG
		// video: https://www.youtube.com/watch?v=g21royNJ4fw
		var lightrag = builder.AddContainer("lightrag", "ghcr.io/hkuds/lightrag")
			.WithReference(ollama)
			.WithBindMount("../../data", "/app/data")
			.WithEnvironment("WORKERS", "2")
			.WithEnvironment("MAX_ASYNC", "1")
			.WithEnvironment("WEBUI_TITLE", "Local LightRAG")
			.WithEnvironment("WEBUI_DESCRIPTION", "Local LightRAG Simple and Fast Graph Based RAG System")
			.WithEnvironment("CHUNK_SIZE", "512")
			.WithEnvironment("CHUNK_OVERLAP", "32")
			.WithEnvironment("LLM_TIMEOUT", "420")
			.WithEnvironment("LLM_BINDING", "ollama")
			.WithEnvironment("LLM_BINDING_HOST", ollama.GetEndpoint("http"))
			.WithEnvironment("LLM_MODEL", aiChatModel.Resource)
			.WithEnvironment("MAX_PARALLEL_INSERT", "2")
			.WithEnvironment("EMBEDDING_FUNC_MAX_ASYNC", "1")
			.WithEnvironment("EMBEDDING_BATCH_NUM", "1")
			.WithEnvironment("EMBEDDING_TIMEOUT", "420")
			.WithEnvironment("EMBEDDING_BINDING_HOST", ollama.GetEndpoint("http"))
			.WithEnvironment("EMBEDDING_MODEL", aiEmbeddings.Resource)
			.WithEnvironment("EMBEDDING_DIM", "1024")
			.WithEnvironment("INPUT_DIR", "/app/data/inputs")
			.WithEnvironment("WORKING_DIR", "/app/data/rag_storage")
			.WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
			.WithEnvironment("NEO4J_USERNAME", neo4jUser.Resource)
			.WithEnvironment("NEO4J_PASSWORD", neo4jPass.Resource)
			.WithEnvironment("NEO4J_DATABASE", "neo4j")
			.WithEnvironment("NEO4J_MAX_CONNECTION_POOL_SIZE", "100")
			.WithEnvironment("NEO4J_CONNECTION_TIMEOUT", "30")
			.WithEnvironment("NEO4J_CONNECTION_ACQUISITION_TIMEOUT", "30")
			.WithEnvironment("NEO4J_MAX_TRANSACTION_RETRY_TIME", "30")
			.WithEnvironment("NEO4J_MAX_CONNECTION_LIFETIME", "420")
			.WithEnvironment("NEO4J_LIVENESS_CHECK_TIMEOUT", "30")
			.WithEnvironment("NEO4J_KEEP_ALIVE", "true")
			.WithEndpoint(9621, 9621)
			.WaitFor(ollama)
			.WaitFor(neo4jDb);

		// Now you can reference it in the web frontend
		builder.AddProject<Projects.AspireApp_Web>("webfrontend")
			.WithExternalHttpEndpoints()
			.WithHttpHealthCheck("/health")
			.WithReference(apiService)
			.WithReference(ollama)
			.WithReference(appmodel)
			.WithEnvironment("AI-Endpoint", aiEndpoint.Resource)
			.WithEnvironment("AI-Chat-Model", aiChatModel.Resource)
			.WithEnvironment("NEO4J_HTTP_URL", neo4jDb.GetEndpoint("http"))		// Neo4j browser endpoint
			.WithEnvironment("NEO4J_BOLT_URL", neo4jDb.GetEndpoint("bolt"))		// Neo4j bolt endpoint
			.WithEnvironment("NEO4J_AUTH", $"neo4j/{neo4jPassValue}")				// Neo4j credentials
			.WithEnvironment("PYTHON_SERVICE_URL", pythonServices.GetEndpoint("http")) // Get Python service endpoint
			.WaitFor(ollama)
			.WaitFor(appmodel)
			.WaitFor(apiService)
			.WaitFor(neo4jDb)
			.WaitFor(pythonServices)
			.WaitFor(lightrag);

		await builder.Build().RunAsync();
    }
}
