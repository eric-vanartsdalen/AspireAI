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
//    .WithReference(pythonServices)
    .WithEnvironment("AI-Endpoint", aiEndpoint)
    .WithEnvironment("AI-Model", aiModel)
    .WithEnvironment("PYTHON_SERVICE_URL", pythonServices.GetEndpoint("http")) // Get Python service endpoint
    .WaitFor(ollama)
    .WaitFor(appmodel)
    .WaitFor(apiService)
    .WaitFor(pythonServices);

await builder.Build().RunAsync();
