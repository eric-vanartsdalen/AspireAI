using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Schema;
using System;
using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

// FRITZ: Config with .NET Aspire
var aiModel = builder.AddParameterFromConfiguration("AI-Model", "AI-Model");
var aiEndpoint = builder.AddParameterFromConfiguration("AI-Endpoint", "AI-Endpoint");


// API Service
var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
	.WithHttpHealthCheck("/health");

// TODO add AI Ollama & AI Model from configs
// SETUP OLLAMA & MODEL CONTAINERS
var ollama = builder.AddOllama("ollama")
	.WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "0.11.4" })
	.WithDataVolume()
	.WithContainerRuntimeArgs("--gpus", "all");
var appmodel = ollama.AddModel("chat", aiModel.Resource.Value);

// BLAZOR app (TODO add references and Chat function later)
// See Blazor Home page... and the variables I pull...
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
	.WithExternalHttpEndpoints()
	.WithHttpHealthCheck("/health")
	.WithReference(apiService)
	.WithReference(ollama)
	.WithReference(appmodel)
	.WithEnvironment("AI-Endpoint", aiEndpoint)
	.WithEnvironment("AI-Model", aiModel)
	.WaitFor(ollama)
	.WaitFor(appmodel)
	.WaitFor(apiService);

await builder.Build().RunAsync();
