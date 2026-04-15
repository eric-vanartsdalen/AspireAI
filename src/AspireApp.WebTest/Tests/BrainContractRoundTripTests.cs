extern alias api;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contracts = api::AspireApp.ApiService.Contracts;

namespace AspireApp.WebTest.Tests;

public sealed class BrainContractRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    [Fact]
    public void CanonicalDocument_Serializes_WithSnakeCaseContractFields()
    {
        var document = new Contracts.CanonicalDocument(
            TenantId: "tenant-a",
            CorrelationId: "corr-001",
            DocumentId: 42,
            SourceType: "upload",
            SourceConfidence: 0.9,
            Pages:
            [
                new Contracts.PageContent(
                    PageNumber: 1,
                    Content: "Contract text",
                    Section: "introduction",
                    Metadata: new JsonObject { ["language"] = "en" })
            ],
            Metadata: new JsonObject { ["kind"] = "textbook" });

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(document, JsonOptions));
        var root = payload.RootElement;

        Assert.True(root.TryGetProperty("tenant_id", out var tenantId));
        Assert.True(root.TryGetProperty("correlation_id", out var correlationId));
        Assert.True(root.TryGetProperty("document_id", out var documentId));
        Assert.True(root.TryGetProperty("source_type", out var sourceType));
        Assert.True(root.TryGetProperty("source_confidence", out var sourceConfidence));
        Assert.True(root.TryGetProperty("pages", out var pages));
        Assert.False(root.TryGetProperty(nameof(Contracts.CanonicalDocument.TenantId), out _));
        Assert.False(root.TryGetProperty(nameof(Contracts.CanonicalDocument.DocumentId), out _));

        Assert.Equal("tenant-a", tenantId.GetString());
        Assert.Equal("corr-001", correlationId.GetString());
        Assert.Equal(42, documentId.GetInt32());
        Assert.Equal("upload", sourceType.GetString());
        Assert.Equal(0.9, sourceConfidence.GetDouble());

        var firstPage = pages[0];
        Assert.Equal(1, firstPage.GetProperty("page_number").GetInt32());
        Assert.Equal("Contract text", firstPage.GetProperty("content").GetString());
        Assert.Equal("introduction", firstPage.GetProperty("section").GetString());
    }

    [Fact]
    public void ValidatedDocument_RoundTrips_FromPythonStyleJson()
    {
        const string pythonJson = """
            {
              "tenant_id": "tenant-a",
              "correlation_id": "corr-validated",
              "document_id": 7,
              "source_type": "upload",
              "source_confidence": 0.91,
              "pages": [
                {
                  "page_number": 1,
                  "content": "First page",
                  "section": "overview",
                  "metadata": {
                    "language": "en"
                  }
                }
              ],
              "metadata": {
                "source_system": "docling"
              },
              "claims": [
                {
                  "claim_id": "claim-1",
                  "text": "Aspire orchestrates the stack.",
                  "confidence": 0.88,
                  "evidence": [
                    {
                      "content": "Aspire AppHost wires the services together.",
                      "confidence": 0.83,
                      "source": "document:7/page:1"
                    }
                  ],
                  "source_ref": "document:7/page:1"
                }
              ],
              "contradictions": [
                {
                  "claim_id": "claim-1",
                  "conflicting_claim_id": "claim-2",
                  "description": "A second source says orchestration is manual.",
                  "confidence": 0.34
                }
              ],
              "overall_confidence": 0.85
            }
            """;

        var contract = JsonSerializer.Deserialize<Contracts.ValidatedDocument>(pythonJson, JsonOptions);

        Assert.NotNull(contract);
        Assert.Equal("tenant-a", contract.TenantId);
        Assert.Equal("corr-validated", contract.CorrelationId);
        Assert.Equal(7, contract.DocumentId);
        Assert.Single(contract.Claims);
        Assert.Single(contract.Contradictions);
        Assert.Equal("claim-1", contract.Claims[0].ClaimId);
        Assert.Equal("document:7/page:1", contract.Claims[0].Evidence[0].Source);
        Assert.Equal(0.85, contract.OverallConfidence);

        AssertJsonEquivalent(pythonJson, JsonSerializer.Serialize(contract, JsonOptions));
    }

    [Fact]
    public void KnowledgeResult_RoundTrips_FromPythonStyleJson()
    {
        const string pythonJson = """
            {
              "tenant_id": "tenant-a",
              "correlation_id": "corr-knowledge",
              "results": [
                {
                  "content": "Aspire AppHost coordinates the web and API projects.",
                  "confidence": 0.86,
                  "source_refs": [
                    "document:7/page:1",
                    "document:7/page:2"
                  ],
                  "relevance_score": 0.93
                }
              ]
            }
            """;

        var contract = JsonSerializer.Deserialize<Contracts.KnowledgeResult>(pythonJson, JsonOptions);

        Assert.NotNull(contract);
        Assert.Equal("tenant-a", contract.TenantId);
        Assert.Equal("corr-knowledge", contract.CorrelationId);
        Assert.Single(contract.Results);
        Assert.Equal(0.93, contract.Results[0].RelevanceScore);
        Assert.Equal("document:7/page:1", contract.Results[0].SourceRefs[0]);

        AssertJsonEquivalent(pythonJson, JsonSerializer.Serialize(contract, JsonOptions));
    }

    [Fact]
    public void ReasonResponse_RoundTrips_FromPythonStyleJson()
    {
        const string pythonJson = """
            {
              "tenant_id": "tenant-a",
              "correlation_id": "corr-reason",
              "answer": "Aspire keeps the app composition explicit.",
              "confidence": 0.89,
              "evidence": [
                {
                  "content": "AppHost registers the API and web projects directly.",
                  "confidence": 0.84,
                  "source": "document:7/page:3"
                }
              ],
              "reasoning_steps": [
                {
                  "step": "retrieve",
                  "reasoning": "Locate orchestration details in the indexed documents.",
                  "tool": "knowledge-query",
                  "result": "Matched AppHost references."
                }
              ],
              "proactive_suggestions": [
                "Review AppHost health check coverage."
              ]
            }
            """;

        var contract = JsonSerializer.Deserialize<Contracts.ReasonResponse>(pythonJson, JsonOptions);

        Assert.NotNull(contract);
        Assert.Equal("tenant-a", contract.TenantId);
        Assert.Equal("corr-reason", contract.CorrelationId);
        Assert.Equal("Aspire keeps the app composition explicit.", contract.Answer);
        Assert.Single(contract.Evidence);
        Assert.Single(contract.ReasoningSteps);
        Assert.Single(contract.ProactiveSuggestions);
        Assert.Equal("knowledge-query", contract.ReasoningSteps[0].Tool);

        AssertJsonEquivalent(pythonJson, JsonSerializer.Serialize(contract, JsonOptions));
    }

    [Fact]
    public void BrainChatRequest_Serializes_WithSnakeCaseAndEnumStrings()
    {
        var request = new Contracts.BrainChatRequest(
            TenantId: "tenant-a",
            CorrelationId: "corr-chat",
            Query: "How does Aspire work?",
            Mode: Contracts.ChatMode.Critique,
            ConversationId: "conv-42",
            TopK: 10);

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        var root = payload.RootElement;

        Assert.True(root.TryGetProperty("query", out var query));
        Assert.True(root.TryGetProperty("mode", out var mode));
        Assert.True(root.TryGetProperty("conversation_id", out var convId));
        Assert.True(root.TryGetProperty("top_k", out var topK));
        Assert.True(root.TryGetProperty("tenant_id", out _));
        Assert.True(root.TryGetProperty("correlation_id", out _));

        Assert.Equal("How does Aspire work?", query.GetString());
        Assert.Equal("critique", mode.GetString());
        Assert.Equal("conv-42", convId.GetString());
        Assert.Equal(10, topK.GetInt32());
    }

    [Fact]
    public void BrainChatRequest_RoundTrips_FromPythonStyleJson()
    {
        const string pythonJson = """
            {
              "tenant_id": "tenant-a",
              "correlation_id": "corr-chat",
              "query": "What is Aspire?",
              "mode": "regular",
              "conversation_id": null,
              "top_k": 5
            }
            """;

        var contract = JsonSerializer.Deserialize<Contracts.BrainChatRequest>(pythonJson, JsonOptions);

        Assert.NotNull(contract);
        Assert.Equal("tenant-a", contract.TenantId);
        Assert.Equal("What is Aspire?", contract.Query);
        Assert.Equal(Contracts.ChatMode.Regular, contract.Mode);
        Assert.Null(contract.ConversationId);
        Assert.Equal(5, contract.TopK);

        AssertJsonEquivalent(pythonJson, JsonSerializer.Serialize(contract, JsonOptions));
    }

    [Fact]
    public async Task BrainChatRequest_RoundTrips_Between_Python_And_CSharp()
    {
        var helperScriptPath = GetRepositoryPath("src", "AspireApp.PythonServices", "tests", "contract_roundtrip_helper.py");
        var pythonJson = await RunPythonHelperAsync(helperScriptPath, "emit-brain-chat-request");

        var contract = JsonSerializer.Deserialize<Contracts.BrainChatRequest>(pythonJson, JsonOptions);

        Assert.NotNull(contract);
        Assert.Equal("tenant-roundtrip", contract.TenantId);
        Assert.Equal("corr-chat-roundtrip", contract.CorrelationId);
        Assert.Equal("How does Aspire orchestrate services?", contract.Query);
        Assert.Equal(Contracts.ChatMode.Critique, contract.Mode);
        Assert.Equal("conv-123", contract.ConversationId);
        Assert.Equal(10, contract.TopK);

        var csharpJson = JsonSerializer.Serialize(contract, JsonOptions);
        var tempFilePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFilePath, csharpJson, TestContext.Current.CancellationToken);
            var normalizedPythonJson = await RunPythonHelperAsync(helperScriptPath, "validate-brain-chat-request", tempFilePath);

            AssertJsonEquivalent(pythonJson, csharpJson);
            AssertJsonEquivalent(csharpJson, normalizedPythonJson);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public async Task CanonicalDocument_RoundTrips_Between_Python_And_CSharp()
    {
        var helperScriptPath = GetRepositoryPath("src", "AspireApp.PythonServices", "tests", "contract_roundtrip_helper.py");
        var pythonJson = await RunPythonHelperAsync(helperScriptPath, "emit-canonical");

        var contract = JsonSerializer.Deserialize<Contracts.CanonicalDocument>(pythonJson, JsonOptions);

        Assert.NotNull(contract);
        Assert.Equal("tenant-roundtrip", contract.TenantId);
        Assert.Equal("corr-roundtrip", contract.CorrelationId);
        Assert.Equal(42, contract.DocumentId);

        var csharpJson = JsonSerializer.Serialize(contract, JsonOptions);
        var tempFilePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFilePath, csharpJson, TestContext.Current.CancellationToken);
            var normalizedPythonJson = await RunPythonHelperAsync(helperScriptPath, "validate-canonical", tempFilePath);

            AssertJsonEquivalent(pythonJson, csharpJson);
            AssertJsonEquivalent(csharpJson, normalizedPythonJson);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    private static void AssertJsonEquivalent(string expectedJson, string actualJson)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected JSON:{Environment.NewLine}{expectedJson}{Environment.NewLine}Actual JSON:{Environment.NewLine}{actualJson}");
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AspireApp.sln")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    private static async Task<string> RunPythonHelperAsync(string scriptPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Python contract helper.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        var standardOutput = (await standardOutputTask).Trim();
        var standardError = (await standardErrorTask).Trim();

        Assert.True(
            process.ExitCode == 0,
            $"Python contract helper failed with exit code {process.ExitCode}.{Environment.NewLine}{standardError}");

        return standardOutput;
    }
}
