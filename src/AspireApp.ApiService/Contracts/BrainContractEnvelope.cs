using System.Text.Json.Serialization;

namespace AspireApp.ApiService.Contracts;

public abstract record BrainContractEnvelope(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("correlation_id")] string CorrelationId);
