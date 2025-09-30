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

// Add a NEO4J container for graph database
var neo4jUser = builder.AddParameter("neo4j-user", "neo4j"); 
var neo4jPass = builder.AddParameter("neo4j-pass", "neo4j@secret");

var neo4jDb = builder.AddDockerfile("graph-db", "../../src/AspireApp.Neo4jService/")
    .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")  // Neo4j browser interface
    .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt")      // Neo4j bolt protocol
    .WithBindMount("../../database/neo4j/data", "/data")               // Data persistence - direct host access
    .WithBindMount("../../database/neo4j/logs", "/logs")               // Logs - direct host access
    .WithBindMount("../../database/neo4j/config", "/config")           // Configuration - direct host access
    .WithBindMount("../../database/neo4j/plugins", "/plugins")         // Plugins - direct host access
    .WithEnvironment("NEO4J_AUTH", $"{neo4jUser.Resource.Value}/{neo4jPass.Resource.Value}")
    .WithHttpHealthCheck("/");

// Setup Python services environment
var pythonServices = builder
    .AddDockerfile("python-service", "../../src/AspireApp.PythonServices/")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
    .WithBindMount("../../data", "/app/data")
    .WithBindMount("../../database", "/app/database")
    .WithHttpHealthCheck("/health");  // Add health check

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
    .WithEnvironment("NEO4J_AUTH", $"neo4j/{neo4jPass.Resource.Value}")  // Neo4j credentials
    .WithEnvironment("PYTHON_SERVICE_URL", pythonServices.GetEndpoint("http")) // Get Python service endpoint
    .WaitFor(ollama)
    .WaitFor(appmodel)
    .WaitFor(apiService)
    .WaitFor(neo4jDb)
    .WaitFor(pythonServices);

await builder.Build().RunAsync();
