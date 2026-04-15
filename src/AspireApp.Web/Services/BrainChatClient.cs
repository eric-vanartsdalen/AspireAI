using System.Net.Http.Json;
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

        using var response = await httpClient.PostAsJsonAsync("brain/chat", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "BRAIN chat failed: {StatusCode} - {Error}",
                (int)response.StatusCode, errorBody);
            throw new BrainChatException($"Gateway returned {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<BrainChatResponse>(cancellationToken: cancellationToken)
            ?? throw new BrainChatException("Gateway returned empty response");

        logger.LogInformation(
            "BRAIN chat response: confidence={Confidence}, evidence={EvidenceCount}",
            result.Confidence, result.Evidence.Count);

        return result;
    }
}

public sealed class BrainChatException(string message) : Exception(message);
