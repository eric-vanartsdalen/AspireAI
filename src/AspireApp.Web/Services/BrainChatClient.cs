using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspireApp.Web.Services;

public interface IBrainChatClient
{
    Task<BrainChatResponse> ChatAsync(
        string query,
        string mode,
        string? tenantId,
        string? conversationId,
        int topK = 5,
        CancellationToken cancellationToken = default);
}

public sealed record BrainChatGatewayRequest(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("conversation_id")] string? ConversationId = null,
    [property: JsonPropertyName("top_k")] int TopK = 5);

public sealed record BrainChatResponse(
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("evidence")] IReadOnlyList<BrainChatEvidence> Evidence,
    [property: JsonPropertyName("reasoning_steps")] IReadOnlyList<BrainChatReasoningStep> ReasoningSteps,
    [property: JsonPropertyName("proactive_suggestions")] IReadOnlyList<string> ProactiveSuggestions);

public sealed record BrainChatEvidence(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("source")] string Source);

public sealed record BrainChatReasoningStep(
    [property: JsonPropertyName("step")] string Step,
    [property: JsonPropertyName("reasoning")] string Reasoning,
    [property: JsonPropertyName("tool")] string? Tool = null,
    [property: JsonPropertyName("result")] string Result = "");

public sealed class BrainChatClient(HttpClient httpClient, ILogger<BrainChatClient> logger) : IBrainChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<BrainChatResponse> ChatAsync(
        string query,
        string mode,
        string? tenantId,
        string? conversationId,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var request = new BrainChatGatewayRequest(
            TenantId: tenantId ?? "default",
            CorrelationId: correlationId,
            Query: query,
            Mode: mode,
            ConversationId: conversationId,
            TopK: topK);

        logger.LogInformation(
            "BRAIN chat request: mode={Mode}, correlation={CorrelationId}",
            mode, correlationId);

        try
        {
            using var response = await httpClient.PostAsJsonAsync("brain/chat", request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var problem = ExtractProblem(responseBody);
                logger.LogWarning(
                    "BRAIN chat failed: {StatusCode} {Title} - {Detail}",
                    (int)response.StatusCode,
                    problem.Title,
                    problem.Detail);

                throw new BrainChatException(
                    string.IsNullOrWhiteSpace(problem.Detail)
                        ? $"Gateway returned {(int)response.StatusCode}."
                        : problem.Detail,
                    (int)response.StatusCode,
                    problem.Title);
            }

            var result = Deserialize<BrainChatResponse>(responseBody)
                ?? throw new BrainChatException("Gateway returned empty response");

            logger.LogInformation(
                "BRAIN chat response: confidence={Confidence}, evidence={EvidenceCount}",
                result.Confidence, result.Evidence.Count);

            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "BRAIN gateway request failed");
            throw new BrainChatException("The BRAIN gateway is unavailable.", innerException: ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "BRAIN gateway returned invalid JSON");
            throw new BrainChatException("The BRAIN gateway returned invalid JSON.", innerException: ex);
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

    private static BrainChatProblem ExtractProblem(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new BrainChatProblem("BRAIN chat failed", "No additional detail was returned.");
        }

        try
        {
            using var payload = JsonDocument.Parse(responseBody);
            return new BrainChatProblem(
                ExtractFirstString(payload.RootElement, "title") ?? "BRAIN chat failed",
                ExtractFirstString(payload.RootElement, "detail", "message", "error") ?? responseBody);
        }
        catch (JsonException)
        {
            return new BrainChatProblem("BRAIN chat failed", responseBody);
        }
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

    private sealed record BrainChatProblem(string Title, string Detail);
}

public sealed class BrainChatException(
    string message,
    int? statusCode = null,
    string? title = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;

    public string? Title { get; } = title;
}
