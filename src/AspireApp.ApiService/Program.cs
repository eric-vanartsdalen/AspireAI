using AspireApp.ApiService.Contracts;
using AspireApp.ApiService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<IBrainBackendClient, PythonBrainBackendClient>((serviceProvider, client) =>
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var configuredBaseAddress = configuration["PYTHON_SERVICE_URL"];
        client.BaseAddress = new Uri(
            string.IsNullOrWhiteSpace(configuredBaseAddress)
                ? "http://localhost:8000/"
                : EnsureTrailingSlash(configuredBaseAddress));
        client.Timeout = TimeSpan.FromMinutes(2);
    })
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(90);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(3);
    });
#pragma warning restore EXTEXP0001

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var brain = app.MapGroup("/brain")
    .WithTags("Brain");

brain.MapPost("/chat", async Task<IResult> (
        BrainChatRequest request,
        IBrainBackendClient backendClient,
        CancellationToken cancellationToken) =>
    {
        var normalizedRequest = NormalizeChatRequest(request);
        if (string.IsNullOrWhiteSpace(normalizedRequest.Query))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["query"] = ["query is required."]
            });
        }

        if (normalizedRequest.TopK <= 0 || normalizedRequest.TopK > 25)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["top_k"] = ["top_k must be between 1 and 25."]
            });
        }

        try
        {
            var response = await backendClient.ChatAsync(normalizedRequest, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (BrainGatewayProblemException ex)
        {
            return ToProblemResult(ex);
        }
    })
    .WithName("BrainChat")
    .Accepts<BrainChatRequest>("application/json")
    .Produces<ReasonResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status501NotImplemented)
    .ProducesProblem(StatusCodes.Status502BadGateway);

brain.MapPost("/ingest", async Task<IResult> (
        BrainIngestRequest request,
        IBrainBackendClient backendClient,
        CancellationToken cancellationToken) =>
    {
        var normalizedRequest = NormalizeIngestRequest(request);
        if (normalizedRequest.DocumentId <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["document_id"] = ["document_id must be greater than 0."]
            });
        }

        try
        {
            var response = await backendClient.TriggerIngestionAsync(normalizedRequest, cancellationToken);
            return TypedResults.Accepted($"/brain/ingest/{response.DocumentId}", response);
        }
        catch (BrainGatewayProblemException ex)
        {
            return ToProblemResult(ex);
        }
    })
    .WithName("BrainIngest")
    .Accepts<BrainIngestRequest>("application/json")
    .Produces<BrainIngestResponse>(StatusCodes.Status202Accepted)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status502BadGateway);

brain.MapPost("/query", async Task<IResult> (
        BrainQueryRequest request,
        IBrainBackendClient backendClient,
        CancellationToken cancellationToken) =>
    {
        var normalizedRequest = NormalizeQueryRequest(request);
        if (string.IsNullOrWhiteSpace(normalizedRequest.Query))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["query"] = ["query is required."]
            });
        }

        if (normalizedRequest.TopK <= 0 || normalizedRequest.TopK > 25)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["top_k"] = ["top_k must be between 1 and 25."]
            });
        }

        try
        {
            var response = await backendClient.QueryKnowledgeAsync(normalizedRequest, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (BrainGatewayProblemException ex)
        {
            return ToProblemResult(ex);
        }
    })
    .WithName("BrainQuery")
    .Accepts<BrainQueryRequest>("application/json")
    .Produces<KnowledgeResult>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status502BadGateway);

brain.MapGet("/health", () => TypedResults.Ok(new BrainHealthResponse(
        Status: "ok",
        Service: "brain-gateway",
        Phase: "2")))
    .WithName("BrainHealth")
    .Produces<BrainHealthResponse>(StatusCodes.Status200OK);

app.MapDefaultEndpoints();

await app.RunAsync();

static string EnsureTrailingSlash(string baseAddress) =>
    baseAddress.EndsWith("/", StringComparison.Ordinal) ? baseAddress : $"{baseAddress}/";

static BrainIngestRequest NormalizeIngestRequest(BrainIngestRequest request) =>
    request with
    {
        TenantId = NormalizeTenantId(request.TenantId),
        CorrelationId = NormalizeCorrelationId(request.CorrelationId)
    };

static BrainQueryRequest NormalizeQueryRequest(BrainQueryRequest request) =>
    request with
    {
        TenantId = NormalizeTenantId(request.TenantId),
        CorrelationId = NormalizeCorrelationId(request.CorrelationId)
    };

static BrainChatRequest NormalizeChatRequest(BrainChatRequest request) =>
    request with
    {
        TenantId = NormalizeTenantId(request.TenantId),
        CorrelationId = NormalizeCorrelationId(request.CorrelationId)
    };

static string NormalizeTenantId(string? tenantId) =>
    string.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId.Trim();

static string NormalizeCorrelationId(string? correlationId) =>
    string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("n") : correlationId.Trim();

static IResult ToProblemResult(BrainGatewayProblemException exception) =>
    Results.Problem(
        statusCode: exception.StatusCode,
        title: exception.Title,
        detail: exception.Message);

internal sealed record BrainHealthResponse(string Status, string Service, string Phase);

public partial class Program;
