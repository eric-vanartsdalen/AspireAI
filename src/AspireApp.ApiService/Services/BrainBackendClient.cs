using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AspireApp.ApiService.Contracts;

namespace AspireApp.ApiService.Services;

public interface IBrainBackendClient
{
    Task<BrainIngestResponse> TriggerIngestionAsync(BrainIngestRequest request, CancellationToken cancellationToken = default);

    Task<KnowledgeResult> QueryKnowledgeAsync(BrainQueryRequest request, CancellationToken cancellationToken = default);

    Task<ReasonResponse> ChatAsync(BrainChatRequest request, CancellationToken cancellationToken = default);
}

public sealed class BrainGatewayProblemException(int statusCode, string title, string detail, Exception? innerException = null)
    : Exception(detail, innerException)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;
}

public sealed class PythonBrainBackendClient(HttpClient httpClient, ILogger<PythonBrainBackendClient> logger) : IBrainBackendClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<PythonBrainBackendClient> _logger = logger;

    public async Task<BrainIngestResponse> TriggerIngestionAsync(
        BrainIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync(
                $"processing/process-document/{request.DocumentId.ToString(CultureInfo.InvariantCulture)}",
                content: null,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProblem(
                    response.StatusCode,
                    "BRAIN ingest failed",
                    $"Python processing could not start document {request.DocumentId}. {ExtractProblemDetail(responseBody)}");
            }

            var payload = Deserialize<ProcessingStartResponse>(responseBody);
            var message = string.IsNullOrWhiteSpace(payload?.Message)
                ? $"Processing started for document {request.DocumentId}"
                : payload.Message;

            return new BrainIngestResponse(
                request.TenantId,
                request.CorrelationId,
                request.DocumentId,
                Status: "processing",
                Message: message);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrainGatewayProblemException(
                StatusCodes.Status504GatewayTimeout,
                "BRAIN ingest timed out",
                $"Python processing did not acknowledge document {request.DocumentId} before the gateway timed out.",
                ex);
        }
    }

    public async Task<KnowledgeResult> QueryKnowledgeAsync(
        BrainQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = await PostForResponseAsync<BrainQueryRequest, KnowledgeResult>(
            "rag/query",
            request,
            "BRAIN query failed",
            cancellationToken);

        return new KnowledgeResult(
            string.IsNullOrWhiteSpace(payload.TenantId) ? request.TenantId : payload.TenantId,
            string.IsNullOrWhiteSpace(payload.CorrelationId) ? request.CorrelationId : payload.CorrelationId,
            payload.Results is { Count: > 0 } results ? results : []);
    }

    public async Task<ReasonResponse> ChatAsync(
        BrainChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = await PostForResponseAsync<BrainChatRequest, ReasonResponse>(
            "brain/chat",
            request,
            "BRAIN chat failed",
            cancellationToken);

        return new ReasonResponse(
            string.IsNullOrWhiteSpace(payload.TenantId) ? request.TenantId : payload.TenantId,
            string.IsNullOrWhiteSpace(payload.CorrelationId) ? request.CorrelationId : payload.CorrelationId,
            payload.Answer,
            payload.Confidence,
            payload.Evidence ?? [],
            payload.ReasoningSteps ?? [],
            payload.ProactiveSuggestions ?? []);
    }

    private async Task<TResponse> PostForResponseAsync<TRequest, TResponse>(
        string relativePath,
        TRequest payload,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(relativePath, payload, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateProblem(
                    response.StatusCode,
                    failureTitle,
                    $"Python retrieval seam {relativePath} returned {(int)response.StatusCode}. {ExtractProblemDetail(responseBody)}");
            }

            try
            {
                var deserialized = Deserialize<TResponse>(responseBody);
                if (deserialized is not null)
                {
                    return deserialized;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Python retrieval seam {RelativePath} returned invalid JSON",
                    relativePath);

                throw new BrainGatewayProblemException(
                    StatusCodes.Status502BadGateway,
                    failureTitle,
                    $"Python retrieval seam {relativePath} returned invalid JSON.",
                    ex);
            }

            _logger.LogWarning("Python retrieval seam {RelativePath} returned an empty response body", relativePath);
            throw new BrainGatewayProblemException(
                StatusCodes.Status502BadGateway,
                failureTitle,
                $"Python retrieval seam {relativePath} returned an empty response.");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrainGatewayProblemException(
                StatusCodes.Status504GatewayTimeout,
                $"{failureTitle} timed out",
                $"Python retrieval seam {relativePath} timed out before it returned a response.",
                ex);
        }
    }

    private static TResponse? Deserialize<TResponse>(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
    }

    private static BrainGatewayProblemException CreateProblem(
        HttpStatusCode statusCode,
        string title,
        string detail)
    {
        var numericStatusCode = (int)statusCode;
        var mappedStatusCode = numericStatusCode is >= StatusCodes.Status400BadRequest and <= 599
            ? numericStatusCode
            : StatusCodes.Status502BadGateway;

        return new BrainGatewayProblemException(mappedStatusCode, title, detail);
    }

    private static string ExtractProblemDetail(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "No additional detail was returned.";
        }

        try
        {
            using var payload = JsonDocument.Parse(responseBody);
            var detail = ExtractFirstString(payload.RootElement, "detail", "message", "error");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return detail;
            }
        }
        catch (JsonException)
        {
        }

        return responseBody;
    }

    private static string? ExtractFirstString(JsonElement payload, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!payload.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var stringValue = value.GetString();
            if (!string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue;
            }
        }

        return null;
    }

    private sealed record ProcessingStartResponse(string? Message);
}
