using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AspireApp.ApiService.Contracts;

public record CanonicalDocument(
    string TenantId,
    string CorrelationId,
    [property: JsonPropertyName("document_id")] int DocumentId,
    [property: JsonPropertyName("source_type")] string SourceType,
    [property: JsonPropertyName("source_confidence")] double SourceConfidence,
    [property: JsonPropertyName("pages")] IReadOnlyList<PageContent> Pages,
    [property: JsonPropertyName("metadata")] JsonObject? Metadata = null)
    : BrainContractEnvelope(TenantId, CorrelationId);

public sealed record ValidatedDocument(
    string TenantId,
    string CorrelationId,
    int DocumentId,
    string SourceType,
    double SourceConfidence,
    IReadOnlyList<PageContent> Pages,
    JsonObject? Metadata,
    [property: JsonPropertyName("claims")] IReadOnlyList<Claim> Claims,
    [property: JsonPropertyName("contradictions")] IReadOnlyList<Contradiction> Contradictions,
    [property: JsonPropertyName("overall_confidence")] double OverallConfidence)
    : CanonicalDocument(TenantId, CorrelationId, DocumentId, SourceType, SourceConfidence, Pages, Metadata);

public sealed record KnowledgeResult(
    string TenantId,
    string CorrelationId,
    [property: JsonPropertyName("results")] IReadOnlyList<KnowledgeItem> Results)
    : BrainContractEnvelope(TenantId, CorrelationId);

public sealed record ReasonResponse(
    string TenantId,
    string CorrelationId,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence,
    [property: JsonPropertyName("reasoning_steps")] IReadOnlyList<ReasoningStep> ReasoningSteps,
    [property: JsonPropertyName("proactive_suggestions")] IReadOnlyList<string> ProactiveSuggestions)
    : BrainContractEnvelope(TenantId, CorrelationId);

public sealed record PageContent(
    [property: JsonPropertyName("page_number")] int PageNumber,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("section")] string? Section = null,
    [property: JsonPropertyName("metadata")] JsonObject? Metadata = null);

public sealed record Claim(
    [property: JsonPropertyName("claim_id")] string ClaimId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("evidence")] IReadOnlyList<Evidence> Evidence,
    [property: JsonPropertyName("source_ref")] string SourceRef);

public sealed record Evidence(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("source")] string Source);

public sealed record Contradiction(
    [property: JsonPropertyName("claim_id")] string ClaimId,
    [property: JsonPropertyName("conflicting_claim_id")] string ConflictingClaimId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("confidence")] double Confidence);

public sealed record KnowledgeItem(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("source_refs")] IReadOnlyList<string> SourceRefs,
    [property: JsonPropertyName("relevance_score")] double RelevanceScore);

public sealed record ReasoningStep(
    [property: JsonPropertyName("step")] string Step,
    [property: JsonPropertyName("reasoning")] string Reasoning,
    [property: JsonPropertyName("tool")] string? Tool = null,
    [property: JsonPropertyName("result")] string Result = "");
