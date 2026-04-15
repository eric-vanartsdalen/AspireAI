extern alias api;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using ApiContracts = api::AspireApp.ApiService.Contracts;
using ApiProgram = api::Program;
using ApiServices = api::AspireApp.ApiService.Services;

namespace AspireApp.WebTest.Tests;

public sealed class BrainGatewayPhase2Tests
{
    [Fact]
    public async Task TriggerIngestionAsync_MapsPythonProcessingResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var handler = new StubHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { message = "Processing started for document 42" })
            }
        ]);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://python-service/")
        };

        var backendClient = new ApiServices.PythonBrainBackendClient(
            httpClient,
            NullLogger<ApiServices.PythonBrainBackendClient>.Instance);

        var response = await backendClient.TriggerIngestionAsync(
            new ApiContracts.BrainIngestRequest("tenant-a", "corr-001", 42),
            cancellationToken);

        Assert.Equal("tenant-a", response.TenantId);
        Assert.Equal("corr-001", response.CorrelationId);
        Assert.Equal(42, response.DocumentId);
        Assert.Equal("processing", response.Status);
        Assert.Equal("Processing started for document 42", response.Message);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("http://python-service/processing/process-document/42", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task QueryKnowledgeAsync_MapsContractShapedKnowledgeResult_FromPythonQueryRoute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var handler = new StubHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ApiContracts.KnowledgeResult(
                    "tenant-a",
                    "corr-knowledge",
                    [
                        new ApiContracts.KnowledgeItem(
                            "Aspire AppHost coordinates the web and API projects.",
                            0.81,
                            ["document:7/page:2", "file:guide.pdf"],
                            0.81)
                    ]))
            }
        ]);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://python-service/")
        };

        var backendClient = new ApiServices.PythonBrainBackendClient(
            httpClient,
            NullLogger<ApiServices.PythonBrainBackendClient>.Instance);

        var response = await backendClient.QueryKnowledgeAsync(
            new ApiContracts.BrainQueryRequest("tenant-a", "corr-knowledge", "Aspire", 3),
            cancellationToken);

        Assert.Equal("tenant-a", response.TenantId);
        Assert.Equal("corr-knowledge", response.CorrelationId);
        Assert.Single(response.Results);
        Assert.Equal("Aspire AppHost coordinates the web and API projects.", response.Results[0].Content);
        Assert.Contains("document:7/page:2", response.Results[0].SourceRefs);
        Assert.Contains("file:guide.pdf", response.Results[0].SourceRefs);
        Assert.Equal(0.81, response.Results[0].Confidence);
        Assert.Equal(0.81, response.Results[0].RelevanceScore);
        Assert.Single(handler.Requests);
        Assert.Equal("http://python-service/rag/query", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task QueryKnowledgeAsync_PassesEnvelopeFields_ToPythonQueryRoute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var handler = new StubHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ApiContracts.KnowledgeResult(
                    "tenant-route",
                    "corr-route",
                    [
                        new ApiContracts.KnowledgeItem(
                            "Tenant-scoped retrieval result",
                            0.72,
                            ["document:7/page:1"],
                            0.72)
                    ]))
            }
        ]);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://python-service/")
        };

        var backendClient = new ApiServices.PythonBrainBackendClient(
            httpClient,
            NullLogger<ApiServices.PythonBrainBackendClient>.Instance);

        _ = await backendClient.QueryKnowledgeAsync(
            new ApiContracts.BrainQueryRequest("tenant-route", "corr-route", "Aspire", 4),
            cancellationToken);

        Assert.Single(handler.Requests);
        Assert.Equal("http://python-service/rag/query", handler.Requests[0].RequestUri?.ToString());

        var requestBody = await handler.Requests[0].Content!.ReadAsStringAsync(cancellationToken);
        using var payload = JsonDocument.Parse(requestBody);

        Assert.Equal("tenant-route", payload.RootElement.GetProperty("tenant_id").GetString());
        Assert.Equal("corr-route", payload.RootElement.GetProperty("correlation_id").GetString());
        Assert.Equal("Aspire", payload.RootElement.GetProperty("query").GetString());
        Assert.Equal(4, payload.RootElement.GetProperty("top_k").GetInt32());
        Assert.False(payload.RootElement.TryGetProperty("chunk_top_k", out _));
        Assert.False(payload.RootElement.TryGetProperty("mode", out _));
    }

    [Fact]
    public async Task QueryKnowledgeAsync_StopsAtPythonQueryRoute_WhenRouteFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var handler = new StubHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = JsonContent.Create(new { detail = "Knowledge layer unavailable" })
            }
        ]);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://python-service/")
        };

        var backendClient = new ApiServices.PythonBrainBackendClient(
            httpClient,
            NullLogger<ApiServices.PythonBrainBackendClient>.Instance);

        var exception = await Assert.ThrowsAsync<ApiServices.BrainGatewayProblemException>(() =>
            backendClient.QueryKnowledgeAsync(
                new ApiContracts.BrainQueryRequest("tenant-a", "corr-query-failed", "Aspire", 3),
                cancellationToken));

        Assert.Equal((int)HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Contains("Knowledge layer unavailable", exception.Message);
        Assert.Single(handler.Requests);
        Assert.Equal("http://python-service/rag/query", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task QueryKnowledgeAsync_ReturnsBadGateway_WhenPythonReturnsInvalidJson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var handler = new StubHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ invalid-json", Encoding.UTF8, "application/json")
            }
        ]);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://python-service/")
        };

        var backendClient = new ApiServices.PythonBrainBackendClient(
            httpClient,
            NullLogger<ApiServices.PythonBrainBackendClient>.Instance);

        var exception = await Assert.ThrowsAsync<ApiServices.BrainGatewayProblemException>(() =>
            backendClient.QueryKnowledgeAsync(
                new ApiContracts.BrainQueryRequest("tenant-a", "corr-invalid-json", "Aspire", 3),
                cancellationToken));

        Assert.Equal((int)HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Contains("invalid JSON", exception.Message);
        Assert.Single(handler.Requests);
        Assert.Equal("http://python-service/rag/query", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task BrainIngest_ReturnsAcceptedPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var backend = new StubBrainBackendClient
        {
            IngestResponse = new ApiContracts.BrainIngestResponse(
                "tenant-a",
                "corr-endpoint",
                77,
                "processing",
                "Queued by gateway")
        };

        await using var factory = new BrainGatewayFactory(backend);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/brain/ingest",
            new ApiContracts.BrainIngestRequest("tenant-a", "corr-endpoint", 77),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiContracts.BrainIngestResponse>(cancellationToken);
        Assert.NotNull(payload);
        Assert.Equal(77, payload.DocumentId);
        Assert.Equal("processing", payload.Status);
        Assert.Equal("Queued by gateway", payload.Message);
        Assert.Equal(77, backend.LastIngestRequest?.DocumentId);
    }

    [Fact]
    public async Task BrainQuery_ReturnsKnowledgeResultPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var backend = new StubBrainBackendClient
        {
            QueryResponse = new ApiContracts.KnowledgeResult(
                "tenant-a",
                "corr-query",
                [
                    new ApiContracts.KnowledgeItem(
                        "Gateway baseline result",
                        0.8,
                        ["document:9/page:1"],
                        0.8)
                ])
        };

        await using var factory = new BrainGatewayFactory(backend);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/brain/query",
            new ApiContracts.BrainQueryRequest("tenant-a", "corr-query", "baseline", 4),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiContracts.KnowledgeResult>(cancellationToken);
        Assert.NotNull(payload);
        Assert.Single(payload.Results);
        Assert.Equal("Gateway baseline result", payload.Results[0].Content);
        Assert.Equal("baseline", backend.LastQueryRequest?.Query);
    }

    [Fact]
    public async Task BrainQuery_RejectsEmptyQuery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var backend = new StubBrainBackendClient();

        await using var factory = new BrainGatewayFactory(backend);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/brain/query",
            new ApiContracts.BrainQueryRequest("tenant-a", "corr-query", string.Empty, 4),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(backend.LastQueryRequest);

        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.True(payload.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("query", out _));
    }

    [Fact]
    public async Task BrainQuery_MapsBackendProblem_ToGatewayProblemResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var backend = new StubBrainBackendClient
        {
            QueryException = new ApiServices.BrainGatewayProblemException(
                StatusCodes.Status502BadGateway,
                "BRAIN query failed",
                "Python retrieval seam rag/query returned invalid JSON.")
        };

        await using var factory = new BrainGatewayFactory(backend);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/brain/query",
            new ApiContracts.BrainQueryRequest("tenant-a", "corr-query", "baseline", 4),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("BRAIN query failed", payload.RootElement.GetProperty("title").GetString());
        Assert.Contains(
            "Python retrieval seam rag/query returned invalid JSON.",
            payload.RootElement.GetProperty("detail").GetString());
    }

    private sealed class BrainGatewayFactory(StubBrainBackendClient backend) : WebApplicationFactory<ApiProgram>
    {
        private readonly StubBrainBackendClient _backend = backend;

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(ApiServices.IBrainBackendClient));
                services.AddSingleton<ApiServices.IBrainBackendClient>(_backend);
            });
        }
    }

    private sealed class StubBrainBackendClient : ApiServices.IBrainBackendClient
    {
        public ApiContracts.BrainIngestRequest? LastIngestRequest { get; private set; }

        public ApiContracts.BrainQueryRequest? LastQueryRequest { get; private set; }

        public ApiContracts.BrainIngestResponse? IngestResponse { get; init; }

        public ApiContracts.KnowledgeResult? QueryResponse { get; init; }

        public ApiServices.BrainGatewayProblemException? IngestException { get; init; }

        public ApiServices.BrainGatewayProblemException? QueryException { get; init; }

        public Task<ApiContracts.BrainIngestResponse> TriggerIngestionAsync(
            ApiContracts.BrainIngestRequest request,
            CancellationToken cancellationToken = default)
        {
            LastIngestRequest = request;
            if (IngestException is not null)
            {
                throw IngestException;
            }

            return Task.FromResult(
                IngestResponse
                ?? new ApiContracts.BrainIngestResponse(request.TenantId, request.CorrelationId, request.DocumentId, "processing", "queued"));
        }

        public Task<ApiContracts.KnowledgeResult> QueryKnowledgeAsync(
            ApiContracts.BrainQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            LastQueryRequest = request;
            if (QueryException is not null)
            {
                throw QueryException;
            }

            return Task.FromResult(
                QueryResponse
                ?? new ApiContracts.KnowledgeResult(request.TenantId, request.CorrelationId, []));
        }

        public Task<ApiContracts.ReasonResponse> ChatAsync(
            ApiContracts.BrainChatRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ApiContracts.ReasonResponse(
                request.TenantId, request.CorrelationId, "stub answer", 0.5, [], [], []));
        }
    }

    private sealed class StubHttpMessageHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CloneRequestAsync(request, cancellationToken));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No stubbed HTTP response was available for the request.");
            }

            return _responses.Dequeue();
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var content = await request.Content.ReadAsStringAsync(cancellationToken);
                var mediaType = request.Content.Headers.ContentType?.MediaType ?? "application/json";
                clone.Content = new StringContent(content, Encoding.UTF8, mediaType);

                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}
