using System.Threading;

// ASPIRE LOCAL SETUP
var builder = DistributedApplication.CreateBuilder(args);

// Config with .NET Aspire
var aiModel = builder.AddParameterFromConfiguration("AI-Model", "AI-Model");
var aiEndpoint = builder.AddParameterFromConfiguration("AI-Endpoint", "AI-Endpoint");

// API Service
var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
	.WithHttpHealthCheck("/health");

// SETUP OLLAMA & MODEL CONTAINERS
var modelName = builder.Configuration["AI-Model"] ?? "phi4-mini:latest";
var ollama = builder.AddOllama("ollama")
    .WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "latest" })
    .WithDataVolume()
    .WithContainerRuntimeArgs("--gpus", "all");
var appmodel = ollama.AddModel("chat", modelName);

// Add a NEO4J container for graph database with caching optimizations
var neo4jUser = builder.AddParameter("neo4j-user", "neo4j"); 
var neo4jPass = builder.AddParameter("neo4j-pass", "neo4j@secret");

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
    // Persistent volumes for caching and data
    .WithVolume("neo4j-data", "/data")                             // Data persistence with volume
    .WithVolume("neo4j-logs", "/logs")                             // Logs persistence with volume
    .WithVolume("neo4j-plugins", "/plugins")                       // Plugin cache with volume
    .WithVolume("neo4j-conf", "/conf")                            // Configuration persistence
    .WithVolume("neo4j-import", "/import")                        // Import cache for bulk operations
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
    .WithVolume("aspire-database", "/app/database")  // Use volume for database persistence
    .WithBindMount("../../database", "/app/host-database")  // Keep host access for debugging/backup
    .WithVolume("python-pip-cache", "/root/.cache/pip")            // Persist pip cache
    .WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
    .WithEnvironment("NEO4J_USER", neo4jUser.Resource)
    .WithEnvironment("NEO4J_PASSWORD", neo4jPass.Resource)
    .WithEnvironment("PIP_CACHE_DIR", "/root/.cache/pip")          // Use persistent pip cache
    .WithEnvironment("DOCKER_BUILDKIT", "1")                      // Enable BuildKit for better caching
    .WithEnvironment("ASPIRE_DB_PATH", "/app/database/data-resources.db")  // Use volume path
    .WithEnvironment("ASPIRE_DB_BACKUP_PATH", "/app/host-database/data-resources.db")  // Backup to host
    .WithHttpHealthCheck("/health")
    .WaitFor(neo4jDb);  // Ensure Neo4j starts before Python service

// Now you can reference it in the web frontend
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WithReference(ollama)
    .WithReference(appmodel)
    .WithEnvironment("AI-Endpoint", aiEndpoint.Resource)
    .WithEnvironment("AI-Model", aiModel.Resource)
    .WithEnvironment("NEO4J_HTTP_URL", neo4jDb.GetEndpoint("http"))      // Neo4j browser endpoint
    .WithEnvironment("NEO4J_BOLT_URL", neo4jDb.GetEndpoint("bolt"))      // Neo4j bolt endpoint
    .WithEnvironment("NEO4J_AUTH", $"neo4j/{neo4jPassValue}")  // Neo4j credentials
    .WithEnvironment("PYTHON_SERVICE_URL", pythonServices.GetEndpoint("http")) // Get Python service endpoint
    .WaitFor(ollama)
    .WaitFor(appmodel)
    .WaitFor(apiService)
    .WaitFor(neo4jDb)
    .WaitFor(pythonServices);

await builder.Build().RunAsync();
