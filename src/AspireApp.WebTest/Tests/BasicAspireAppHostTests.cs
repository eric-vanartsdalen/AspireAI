using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Playwright;
using Npgsql;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.v3.Priority;

namespace AspireApp.WebTest.Tests;

[TestCaseOrderer(typeof(PriorityOrderer))]
public class BasicAspireAppHostTests : IClassFixture<TestFixture>
{
    private const string AspireTableNameColumn = "table tbody tr td[col-index='1']";
    private const string AspireTableResourceColumn = "table tbody tr td[col-index='4']";
    private const string AspireTableLinksColumn = "table tbody tr td[col-index='5']";
    private const string FileNameCell = "table[class='file-table'] tbody tr td[class='file-name-cell']";
    private const string FullFilenameCell = "span[class='file-name-full']";
    private const string FileTableRow = "table[class='file-table'] tbody tr";
    private const string FilenameCellInRow = "td[class='file-name-cell'] span[class='file-name-full']";
    private const string DeleteButtonInCell = "td button";
    private const string DemoProviderId = "demo";
    private const string DemoUserId = "demo-taylor-jones";
    private const string SmokeRoundTripQuery = "AspireAI smoke test";
    private static readonly string TestFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "AspireApp.WebTest", "DataExample", "processing-smoke.pdf");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PythonVisibilityTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WebApiTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PythonVisibilityPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ProcessingPollTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessingPollInterval = TimeSpan.FromSeconds(1);
    private readonly AppHostMappingModel _data;
    private readonly IBrowser _browser;

    public BasicAspireAppHostTests(TestFixture fixture)
    {
        _data = fixture.AppHostMapping;
        Assert.NotNull(_data.Browser);
        _browser = _data.Browser!;
    }

    [Fact, Priority(0)]
    public async Task AspireDashboardLoads()
    {
        Assert.False(string.IsNullOrWhiteSpace(_data.AspireDashboardLoginUri));

        await WithPageAsync(async page =>
        {
            // Navigate to the tokenized login URI. The dashboard validates the
            // browser token, sets an auth cookie, and redirects to the root page.
            await page.GotoAsync(_data.AspireDashboardLoginUri, _data.Options);
            await WaitForPageLoadCompletion(page);

            // Wait for the post-authentication redirect to leave /login.
            // If GotoAsync already followed a server-side 302 this resolves immediately;
            // otherwise it waits for the Blazor-driven redirect after token validation.
            await page.WaitForURLAsync(
                url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = 120_000 });

            // Blazor Server sets the page title asynchronously via <PageTitle>
            // after the SignalR circuit initializes. Give it generous time.
            await page.WaitForFunctionAsync(
                "() => document.title && document.title.length > 0",
                null,
                new PageWaitForFunctionOptions { Timeout = 60_000 });

            await WaitForPageLoadCompletion(page);
            var title = await page.TitleAsync();
            if (!title.Equals("AspireApp resources", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"Page title is not as expected: {title}");
                await Task.Delay(2000);
                title = await page.TitleAsync();
            }

            Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Equivalent("AspireApp resources", title);

            await page.Locator(AspireTableNameColumn).First.WaitForAsync(new LocatorWaitForOptions()
            {
                Timeout = 5000
            });
            List<ILocator> rowNames = (await page.Locator(AspireTableNameColumn).AllAsync()).ToList();
            List<ILocator> rowResources = (await page.Locator(AspireTableResourceColumn).AllAsync()).ToList();
            List<ILocator> rowLinks = (await page.Locator(AspireTableLinksColumn).AllAsync()).ToList();
            string ollamaLink = string.Empty;
            string webFrontendLink = string.Empty;
            string graphDbLink = string.Empty;
            string lightRagLink = string.Empty;
            string pythonServiceLink = string.Empty;
            string brainGatewayLink = string.Empty;

            for (int i = 0; i < rowNames.Count; i++)
            {
                var name = await rowNames[i].TextContentAsync();
                var resource = await rowResources[i].TextContentAsync();
                var link = await rowLinks[i].TextContentAsync();
                var message = $"{name.Trim()} | {resource.Trim()} | {link.Trim()}";
                Console.WriteLine(message);
                if (name.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
                {
                    ollamaLink = link.Trim();
                }
                if (name.Contains("WebFrontend", StringComparison.OrdinalIgnoreCase))
                {
                    webFrontendLink = link.Trim();
                }
                if (name.Contains("Graph-Db", StringComparison.OrdinalIgnoreCase))
                {
                    graphDbLink = link.Trim();
                }
                if (name.Contains("LightRag", StringComparison.OrdinalIgnoreCase))
                {
                    lightRagLink = link.Trim();
                }
                if (name.Contains("Python-Service", StringComparison.OrdinalIgnoreCase))
                {
                    pythonServiceLink = link.Trim();
                }
                if (name.Contains("Brain-Gateway", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("BrainGateway", StringComparison.OrdinalIgnoreCase))
                {
                    brainGatewayLink = link.Trim();
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(ollamaLink), "Ollama link should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(webFrontendLink), "Web Frontend link should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(graphDbLink), "Graph-Db link should not be empty");
            Assert.False(string.IsNullOrEmpty(pythonServiceLink), "PythonService link should not be empty");
            Assert.False(string.IsNullOrEmpty(brainGatewayLink), "Brain Gateway link should not be empty");

            Assert.True(ollamaLink.Contains(_data.OllamaUri, StringComparison.OrdinalIgnoreCase),
                $"Ollama link ({ollamaLink}) should contain {_data.OllamaUri}");
            Assert.True(webFrontendLink.Contains(_data.WebfrontendUri, StringComparison.OrdinalIgnoreCase),
                $"Web Frontend link ({webFrontendLink}) should contain {_data.WebfrontendUri}");
            Assert.True(graphDbLink.Contains(_data.GraphDBUri, StringComparison.OrdinalIgnoreCase),
                $"Graph-Db link ({graphDbLink}) should contain {_data.GraphDBUri}");
            if (!string.IsNullOrWhiteSpace(lightRagLink))
            {
                Assert.True(lightRagLink.Contains(_data.LightRagUri, StringComparison.OrdinalIgnoreCase),
                    $"LightRag link ({lightRagLink}) should contain {_data.LightRagUri}");
            }
            Assert.True(pythonServiceLink.Contains(_data.PythonServiceUri, StringComparison.OrdinalIgnoreCase),
                $"PythonService link ({pythonServiceLink}) should contain {_data.PythonServiceUri}");
            Assert.True(brainGatewayLink.Contains(_data.BrainGatewayUri, StringComparison.OrdinalIgnoreCase),
                $"Brain Gateway link ({brainGatewayLink}) should contain {_data.BrainGatewayUri}");
        });
    }

    [Fact, Priority(2)]
    public async Task WebHomeUILoads()
    {
        Console.WriteLine($"Navigating to Web Frontend at: {_data.WebfrontendUri}");
        await WithPageAsync(async page =>
        {
            await page.GotoAsync(_data.WebfrontendUri, _data.Options);
            await WaitForPageLoadCompletion(page);
            var title = await page.TitleAsync();
            Assert.Equivalent("Home", title);
            await page.GetByRole(AriaRole.Heading, new() { Name = "Turn documents into a tenant-aware AI workspace" })
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        });
    }

    [Fact, Priority(2)]
    public async Task OllamaLoads()
    {
        Console.WriteLine($"Navigating to Ollama Frontend at: {_data.OllamaUri}");
        await WithPageAsync(async page =>
        {
            await page.GotoAsync(_data.OllamaUri, _data.Options);
            await WaitForPageLoadCompletion(page);
            var content = await page.ContentAsync();
            Assert.Contains("Ollama is running", content, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact, Priority(2)]
    public async Task PythonServiceLoads()
    {
        Console.WriteLine($"Navigating to Python Services Frontend at: {_data.PythonServiceUri}");
        await WithPageAsync(async page =>
        {
            await page.GotoAsync(_data.PythonServiceUri, _data.Options);
            await WaitForPageLoadCompletion(page);
            var content = await page.ContentAsync();
            Assert.Contains("AspireAI Document Processing Service", content, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact, Priority(2)]
    public async Task PythonServiceOpenAPILoads()
    {
        var PythonOpenAPIEndpoint = $"{_data.PythonServiceUri.TrimEnd('/')}/docs";
        Console.WriteLine($"Navigating to Python Services Open API at: {PythonOpenAPIEndpoint}");
        await WithPageAsync(async page =>
        {
            await page.GotoAsync(PythonOpenAPIEndpoint, _data.Options);
            await WaitForPageLoadCompletion(page);
            var title = await page.TitleAsync();
            var content = await page.ContentAsync();
            Assert.Equivalent("AspireAI Document Processing Service - Swagger UI", title);
            Assert.Contains("AspireAI Document Processing Service", content, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact, Priority(2)]
    public async Task GraphDbLoads()
    {
        Console.WriteLine($"Navigating to Graph DB Services Frontend at: {_data.GraphDBUri}");
        await WithPageAsync(async page =>
        {
            await page.GotoAsync(_data.GraphDBUri, _data.Options);
            await WaitForPageLoadCompletion(page);
            var title = await page.TitleAsync();
            Assert.Equivalent("Neo4j Browser", title);
        });
    }

    [Fact, Priority(1)]
    public async Task FlowEndToEnd()
    {
        var clientInfo = await CreateWebFrontendHttpClientAsync();
        using var webClient = clientInfo.Client;
        await DeleteExistingTestUploadsAsync(webClient);

        await WithPageAsync(async page =>
        {
            await page.GotoAsync(_data.WebfrontendUri, _data.Options);
            await WaitForPageLoadCompletion(page);
            await SignInAsDemoUserAsync(page);

            await ClickByRole(AriaRole.Link, "Upload Documents", page);
            await SetUploadInput(TestFile, AriaRole.Button, "Choose File", page);

            var uploadButton = page.GetByRole(AriaRole.Button, new() { Name = "Start Upload" });
            await WaitForLocator(uploadButton);
            await WaitForUploadButtonEnabledAsync(uploadButton);
            await uploadButton.ClickAsync();
            await WaitForPageLoadCompletion(page);

            var filePrefix = Path.GetFileNameWithoutExtension(TestFile);
            var uploadedRow = await WaitForUploadSuccessAsync(page, filePrefix);
            Assert.Contains(filePrefix, uploadedRow, StringComparison.OrdinalIgnoreCase);

            var uploadedFile = await WaitForUploadedFileByPrefixAsync(webClient, filePrefix, 60000);
            var originalTestFileName = PullFilename(TestFile);

            Assert.True(uploadedFile.Id > 0,
                "Upload completed, but the API-backed file state did not expose a valid document id.");
            Assert.False(string.IsNullOrWhiteSpace(uploadedFile.FileName),
                "Upload completed, but the API-backed file state did not expose the stored file name.");
            Assert.Equal(originalTestFileName, uploadedFile.OriginalFileName);
            Assert.Equal("upload", uploadedFile.SourceType);
            Assert.True(
                new[] { "uploaded", "processing", "processed" }.Contains(uploadedFile.Status, StringComparer.OrdinalIgnoreCase),
                $"Upload completed, but the initial status '{uploadedFile.Status}' was unexpected.");

            var documentId = uploadedFile.Id;
            await WaitForUploadedFileRowAsync(page, uploadedFile.FileName!);

            using var pythonClient = CreatePythonServiceHttpClient();
            var finalStatus = await PollForProcessingCompletionAsync(pythonClient, documentId);
            Assert.Equal("processed", finalStatus.Status);
            Assert.NotNull(finalStatus.StartedAt);
            Assert.NotNull(finalStatus.CompletedAt);
            Assert.True(finalStatus.TotalPages > 0,
                $"Python processing status for document {documentId} did not report extracted pages. Payload: {finalStatus.RawJson}");
            Assert.True(finalStatus.ProcessedPages > 0,
                $"Python processing status for document {documentId} did not report processed pages. Payload: {finalStatus.RawJson}");

            var finalDocument = await WaitForPythonDocumentVisibleAsync(pythonClient, documentId);
            Assert.Equal(uploadedFile.FileName, finalDocument.FileName);
            Assert.Equal(uploadedFile.OriginalFileName, finalDocument.OriginalFilename);
            Assert.Equal("processed", finalDocument.ProcessingStatus);

            var finalUploadState = await WaitForUploadedFileStatusAsync(webClient, documentId, "processed");
            Assert.Equal("processed", finalUploadState.Status);

            var artifacts = await WaitForProcessedArtifactsAsync(documentId);
            using var lightRagClient = CreateLightRagHttpClient();
            await WaitForLightRagIngestionAsync(lightRagClient, artifacts);
            Assert.True(File.Exists(artifacts.DocumentJsonPath),
                $"Expected Docling document artifact at '{artifacts.DocumentJsonPath}', but it was not created.");
            Assert.True(File.Exists(artifacts.FirstPagePath),
                $"Expected at least one page artifact for document {documentId}, but none were created under '{Path.GetDirectoryName(artifacts.FirstPagePath)}'.");
            Assert.True(File.Exists(artifacts.MarkdownPath),
                $"Expected exported markdown artifact for document {documentId}, but none were created under '{Path.GetDirectoryName(artifacts.MarkdownPath)}'.");
            Assert.True(File.Exists(artifacts.MetadataPath),
                $"Expected processing metadata artifact for document {documentId}, but it was not created at '{artifacts.MetadataPath}'.");
            Assert.True(artifacts.LightRagScanRequested,
                $"Expected metadata at '{artifacts.MetadataPath}' to record a LightRAG scan request for document {documentId}.");
            Assert.False(string.IsNullOrWhiteSpace(artifacts.LightRagStagedInputPath),
                $"Expected metadata at '{artifacts.MetadataPath}' to record the staged LightRAG input path for document {documentId}.");
        });
    }

    [Fact, Priority(2)]
    public async Task LiveLightRagNeo4jQueryRoundTrip()
    {
        var clientInfo = await CreateWebFrontendHttpClientAsync();
        using var webClient = clientInfo.Client;
        await DeleteExistingTestUploadsAsync(webClient);
        using var graphDbClient = CreateGraphDbHttpClient();
        var baselineLightRagGraphState = await QueryNeo4jLightRagGraphAsync(graphDbClient);

        var uploadedFile = await UploadTestFileViaApiAsync(webClient);
        using var pythonClient = CreatePythonServiceHttpClient();
        var finalStatus = await PollForProcessingCompletionAsync(pythonClient, uploadedFile.Id);
        Assert.Equal("processed", finalStatus.Status);

        var artifacts = await WaitForProcessedArtifactsAsync(uploadedFile.Id);
        using var lightRagClient = CreateLightRagHttpClient();
        await WaitForLightRagIngestionAsync(lightRagClient, artifacts, timeoutMs: 300000);

        var knowledgeQuery = SmokeRoundTripQuery;

        var graphState = await QueryNeo4jDocumentGraphAsync(graphDbClient, uploadedFile.Id);
        Assert.Equal(1, graphState.DocumentCount);
        Assert.True(graphState.PageCount > 0,
            $"Expected at least one Neo4j page node for document {uploadedFile.Id}, but the graph query returned {graphState.PageCount}.");
        var lightRagGraphState = await WaitForLightRagNeo4jGraphGrowthAsync(graphDbClient, baselineLightRagGraphState);
        Assert.True(
            lightRagGraphState.NodeCount > baselineLightRagGraphState.NodeCount ||
            lightRagGraphState.RelationshipCount > baselineLightRagGraphState.RelationshipCount,
            $"Expected LightRAG-managed Neo4j graph state to grow after ingesting document {uploadedFile.Id}, but baseline nodes/relationships were {baselineLightRagGraphState.NodeCount}/{baselineLightRagGraphState.RelationshipCount} and the latest state is {lightRagGraphState.NodeCount}/{lightRagGraphState.RelationshipCount}.");

        var lightRagResult = await WaitForKnowledgeQueryResultAsync(
            pythonClient,
            "rag/lightrag-query",
            new
            {
                query = knowledgeQuery,
                mode = "mix",
                top_k = 3,
                chunk_top_k = 3,
                include_references = true,
                include_chunk_content = true,
                tenant_id = clientInfo.TenantId,
                correlation_id = $"live-lightrag-{uploadedFile.Id}"
            });

        Assert.NotEmpty(lightRagResult.Results);
        Assert.Contains(lightRagResult.Results, item =>
            item.SourceRefs.Any(sourceRef =>
                sourceRef.Contains($"document:{uploadedFile.Id}", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(uploadedFile.FileName) &&
                 sourceRef.Contains(uploadedFile.FileName, StringComparison.OrdinalIgnoreCase))) ||
            ContainsSignificantQueryTerm(item.Content, knowledgeQuery));

        using var brainGatewayClient = CreateBrainGatewayHttpClient();
        var gatewayResult = await WaitForKnowledgeQueryResultAsync(
            brainGatewayClient,
            "brain/query",
            new
            {
                tenant_id = clientInfo.TenantId,
                correlation_id = $"live-brain-query-{uploadedFile.Id}",
                query = knowledgeQuery,
                top_k = 3
            });

        Assert.NotEmpty(gatewayResult.Results);
        Assert.Contains(gatewayResult.Results, item =>
            item.SourceRefs.Any(sourceRef =>
                sourceRef.Contains($"document:{uploadedFile.Id}", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(uploadedFile.FileName) &&
                 sourceRef.Contains(uploadedFile.FileName, StringComparison.OrdinalIgnoreCase))) ||
            ContainsSignificantQueryTerm(item.Content, knowledgeQuery));
    }

    [Fact, Priority(2)]
    [Trait("Category", "P2-B")]
    public async Task BrainQueryReturnsConfidenceEnrichedResults()
    {
        var clientInfo = await CreateWebFrontendHttpClientAsync();
        using var webClient = clientInfo.Client;
        await DeleteExistingTestUploadsAsync(webClient);
        var uploadedFile = await UploadTestFileViaApiAsync(webClient);
        using var pythonClient = CreatePythonServiceHttpClient();
        var finalStatus = await PollForProcessingCompletionAsync(pythonClient, uploadedFile.Id);
        Assert.Equal("processed", finalStatus.Status);

        var artifacts = await WaitForProcessedArtifactsAsync(uploadedFile.Id);
        using var lightRagClient = CreateLightRagHttpClient();
        await WaitForLightRagIngestionAsync(lightRagClient, artifacts, timeoutMs: 300000);

        using var brainGatewayClient = CreateBrainGatewayHttpClient();
        var gatewayResult = await WaitForKnowledgeQueryResultAsync(
            brainGatewayClient,
            "brain/query",
            new
            {
                tenant_id = clientInfo.TenantId,
                correlation_id = $"p2b-confidence-{uploadedFile.Id}",
                query = SmokeRoundTripQuery,
                top_k = 3
            });

        Assert.NotEmpty(gatewayResult.Results);

        var uploadedDocResults = gatewayResult.Results
            .Where(item => item.SourceRefs.Any(sourceRef =>
                sourceRef.Contains($"document:{uploadedFile.Id}", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(uploadedFile.FileName) &&
                 sourceRef.Contains(uploadedFile.FileName, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        Assert.NotEmpty(uploadedDocResults);

        foreach (var result in uploadedDocResults)
        {
            Assert.NotEqual(0.5, result.Confidence);
        }
    }

    [Fact, Priority(3)]
    public async Task DeleteUploadedTestFile()
    {
        var clientInfo = await CreateWebFrontendHttpClientAsync();
        using var webClient = clientInfo.Client;
        await DeleteExistingTestUploadsAsync(webClient);
        var uploadedFile = await UploadTestFileViaApiAsync(webClient);
        using var pythonClient = CreatePythonServiceHttpClient();
        var finalStatus = await PollForProcessingCompletionAsync(pythonClient, uploadedFile.Id);
        Assert.Equal("processed", finalStatus.Status);
        var artifacts = await WaitForProcessedArtifactsAsync(uploadedFile.Id);
        using var lightRagClient = CreateLightRagHttpClient();
        await WaitForLightRagIngestionAsync(lightRagClient, artifacts);

        await WithPageAsync(async page =>
        {
            await page.GotoAsync(_data.WebfrontendUri, _data.Options);
            await WaitForPageLoadCompletion(page);
            await SignInAsDemoUserAsync(page);
            await ClickByRole(AriaRole.Link, "Upload Documents", page);
            await WaitForUploadedFileRowAsync(page, uploadedFile.FileName!);

            IReadOnlyList<ILocator> filenameRows = await GetDocumentSourceTableRows(page);
            var targetFileToDelete = await PullFileDeleteButton(filenameRows, uploadedFile.FileName!);
            Assert.NotNull(targetFileToDelete);

            await targetFileToDelete.HoverAsync();
            await targetFileToDelete.ClickAsync(new LocatorClickOptions() { Delay = 250 });
            await WaitForPageLoadCompletion(page);
            await WaitForUploadedFileRowRemovedAsync(page, uploadedFile.FileName!);
            await WaitForUploadedFileRemovedFromApiAsync(webClient, uploadedFile.Id);
            await WaitForDeletedArtifactsAsync(uploadedFile, artifacts);

            IReadOnlyList<ILocator> updatedFilenameRows = await GetDocumentSourceTableRows(page);
            foreach (var fileCell in updatedFilenameRows)
            {
                var cellText = await fileCell.Locator(FullFilenameCell).TextContentAsync();
                Assert.False(string.Equals(cellText, uploadedFile.FileName, StringComparison.OrdinalIgnoreCase),
                    $"File {cellText} should have been deleted but still appears in the list.");
                Debug.WriteLine(cellText);
            }
        });

        using var deletedDocumentResponse = await pythonClient.GetAsync($"documents/{uploadedFile.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, deletedDocumentResponse.StatusCode);
    }

    private async Task WithPageAsync(Func<IPage, Task> testAction)
    {
        await using var browserContext = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });

        var page = await browserContext.NewPageAsync();

        try
        {
            await testAction(page);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task SignInAsDemoUserAsync(IPage page)
    {
        var providerButton = page.Locator("[data-testid='auth-provider-demo']");
        await providerButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await providerButton.ClickAsync(new LocatorClickOptions { Delay = 250 });
        var signInButton = page.Locator("[data-testid='auth-submit-sign-in']");
        await signInButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await signInButton.ClickAsync(new LocatorClickOptions { Delay = 250 });
        await WaitForPageLoadCompletion(page);

        var signedInSurface = page.Locator("[data-testid='auth-summary'], [data-testid='auth-user-display'], #tenant-select, h1");
        await WaitForLocator(signedInSurface.First, 30_000);
    }

    private async Task<AuthenticatedClient> CreateWebFrontendHttpClientAsync()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{_data.WebfrontendUri.TrimEnd('/')}/"),
            Timeout = WebApiTimeout
        };

        await AuthenticateAsync(client);
        var tenantId = await ResolveDefaultTenantIdAsync();
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

        return new AuthenticatedClient(client, tenantId);
    }

    private HttpClient CreatePythonServiceHttpClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri($"{_data.PythonServiceUri.TrimEnd('/')}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private HttpClient CreateLightRagHttpClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri($"{_data.LightRagUri.TrimEnd('/')}/"),
            Timeout = WebApiTimeout
        };
    }

    private HttpClient CreateBrainGatewayHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"{_data.BrainGatewayUri.TrimEnd('/')}/"),
            Timeout = WebApiTimeout
        };
    }

    private HttpClient CreateGraphDbHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri($"{_data.GraphDBUri.TrimEnd('/')}/"),
            Timeout = WebApiTimeout
        };
        var encodedCredentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("neo4j:neo4j@secret"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
        return client;
    }

    private async Task AuthenticateAsync(HttpClient webClient)
    {
        var signInUri = $"auth/mock/signin?providerId={DemoProviderId}&userId={Uri.EscapeDataString(DemoUserId)}&returnUrl=%2F";
        using var response = await webClient.GetAsync(signInUri, TestContext.Current.CancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Found)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException($"Mock sign-in failed with {(int)response.StatusCode}. Response: {body}");
        }
    }

    private async Task<string> ResolveDefaultTenantIdAsync()
    {
        await using var connection = new NpgsqlConnection(_data.UploadStoreConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("""
            SELECT tenant_id
            FROM tenant_memberships
            WHERE user_id = @userId AND is_default = TRUE
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("userId", DemoUserId);

        var tenantId = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("Mock sign-in did not create a default tenant membership.");
        }

        return tenantId;
    }

    private async Task DeleteExistingTestUploadsAsync(HttpClient webClient, string? targetFilePath = null)
    {
        using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
        var listBody = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(listResponse.IsSuccessStatusCode,
            $"Could not query existing uploads before FlowEndToEnd. GET '{BuildAbsoluteUri(_data.WebfrontendUri, "api/FileUpload")}' returned {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}. Response: {listBody}");

        var listResult = DeserializeJson<UploadedFilesApiResponse>(listBody, "GET /api/FileUpload");
        Assert.True(listResult.Success, $"Existing upload query returned success=false. Response: {listBody}");

        var testFileName = PullFilename(targetFilePath ?? TestFile);
        var testFilePrefix = Path.GetFileNameWithoutExtension(testFileName);
        using var lightRagClient = CreateLightRagHttpClient();

        foreach (var existingFile in listResult.Files.Where(file =>
                     string.Equals(file.SourceType, "upload", StringComparison.OrdinalIgnoreCase) &&
                     (string.Equals(file.OriginalFileName, testFileName, StringComparison.OrdinalIgnoreCase) ||
                      (!string.IsNullOrWhiteSpace(file.FileName) &&
                       file.FileName.StartsWith(testFilePrefix, StringComparison.OrdinalIgnoreCase)))))
        {
            using var deleteResponse = await webClient.DeleteAsync($"api/FileUpload/{existingFile.Id}", TestContext.Current.CancellationToken);
            var deleteBody = await deleteResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(deleteResponse.IsSuccessStatusCode,
                $"Pre-test cleanup failed for document {existingFile.Id} ('{existingFile.FileName}'). DELETE '{BuildAbsoluteUri(_data.WebfrontendUri, $"api/FileUpload/{existingFile.Id}")}' returned {(int)deleteResponse.StatusCode} {deleteResponse.ReasonPhrase}. Response: {deleteBody}");

            await WaitForUploadedFileRemovedFromApiAsync(webClient, existingFile.Id);
            await WaitForLightRagPipelineIdleAsync(lightRagClient);
        }
    }

    private async Task<UploadedFileApiModel> WaitForUploadedFileAsync(HttpClient webClient, string targetFilePath, int timeoutMs = 60000)
    {
        var waitStopwatch = Stopwatch.StartNew();
        var testFileName = PullFilename(targetFilePath);
        string lastPayload = "<no upload state returned>";

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
            lastPayload = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(listResponse.IsSuccessStatusCode,
                $"Upload state query '{BuildAbsoluteUri(_data.WebfrontendUri, "api/FileUpload")}' returned {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}. Response: {lastPayload}");

            var listResult = DeserializeJson<UploadedFilesApiResponse>(lastPayload, "GET /api/FileUpload");
            Assert.True(listResult.Success, $"Upload state query returned success=false. Response: {lastPayload}");

            var uploadedFile = listResult.Files
                .Where(file =>
                    string.Equals(file.SourceType, "upload", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(file.OriginalFileName, testFileName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.Id)
                .FirstOrDefault();

            if (uploadedFile is not null)
            {
                return uploadedFile;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeoutMs}ms waiting for '{testFileName}' to appear in API-backed upload state. Last payload: {lastPayload}");
        return default!;
    }

    private async Task<UploadedFileApiModel> WaitForUploadedFileByPrefixAsync(HttpClient webClient, string filePrefix, int timeoutMs)
    {
        var waitStopwatch = Stopwatch.StartNew();
        string lastPayload = "<no upload state returned>";

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
            lastPayload = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(listResponse.IsSuccessStatusCode,
                $"Upload state query '{BuildAbsoluteUri(_data.WebfrontendUri, "api/FileUpload")}' returned {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}. Response: {lastPayload}");

            var listResult = DeserializeJson<UploadedFilesApiResponse>(lastPayload, "GET /api/FileUpload");
            Assert.True(listResult.Success, $"Upload state query returned success=false. Response: {lastPayload}");

            var uploadedFile = listResult.Files
                .Where(file => string.Equals(file.SourceType, "upload", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.Id)
                .FirstOrDefault(file =>
                    !string.IsNullOrWhiteSpace(file.FileName) &&
                    file.FileName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase));

            if (uploadedFile is not null)
            {
                return uploadedFile;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out after {timeoutMs}ms waiting for an uploaded file with prefix '{filePrefix}'. Last payload: {lastPayload}");
        return default!;
    }

    private async Task<UploadedFileApiModel> UploadTestFileViaApiAsync(HttpClient webClient, string? filePath = null)
    {
        var resolvedFilePath = string.IsNullOrWhiteSpace(filePath) ? TestFile : filePath;
        await using var fileStream = File.OpenRead(resolvedFilePath);
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(streamContent, "file", Path.GetFileName(resolvedFilePath));

        using var uploadResponse = await webClient.PostAsync("api/FileUpload", form, TestContext.Current.CancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(uploadResponse.IsSuccessStatusCode,
            $"Seed upload API returned {(int)uploadResponse.StatusCode} {uploadResponse.ReasonPhrase}. Response: {uploadBody}");

        var uploadResult = DeserializeJson<FileUploadApiResponse>(uploadBody, "POST /api/FileUpload");
        Assert.True(uploadResult.Success, $"Seed upload returned success=false. Response: {uploadBody}");
        Assert.False(uploadResult.IsDuplicate, $"Seed upload unexpectedly returned a duplicate response. Response: {uploadBody}");

        return await WaitForUploadedFileAsync(webClient, resolvedFilePath);
    }

    private async Task WaitForUploadedFileRemovedFromApiAsync(HttpClient webClient, int documentId, int timeoutMs = 30000)
    {
        var waitStopwatch = Stopwatch.StartNew();
        string lastPayload = "<no upload state returned>";

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
            lastPayload = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(listResponse.IsSuccessStatusCode,
                $"Upload state query '{BuildAbsoluteUri(_data.WebfrontendUri, "api/FileUpload")}' returned {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}. Response: {lastPayload}");

            var listResult = DeserializeJson<UploadedFilesApiResponse>(lastPayload, "GET /api/FileUpload");
            Assert.True(listResult.Success, $"Upload state query returned success=false. Response: {lastPayload}");

            if (listResult.Files.All(file => file.Id != documentId))
            {
                return;
            }

            await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeoutMs}ms waiting for document {documentId} to disappear from API-backed upload state. Last payload: {lastPayload}");
    }

    private async Task WaitForDeletedArtifactsAsync(UploadedFileApiModel uploadedFile, ProcessedArtifactsInfo artifacts)
    {
        var originalUploadPath = Path.Combine(_data.SharedDataPath, uploadedFile.FileName!);
        var processedDocumentDirectory = Path.Combine(_data.SharedDataPath, "processed", "documents", uploadedFile.Id.ToString());
        var stagedInputPath = artifacts.LightRagStagedInputPath;
        var pollStopwatch = Stopwatch.StartNew();

        while (pollStopwatch.Elapsed < ProcessingPollTimeout)
        {
            var originalDeleted = !File.Exists(originalUploadPath);
            var processedDeleted = !Directory.Exists(processedDocumentDirectory);
            var stagedDeleted = string.IsNullOrWhiteSpace(stagedInputPath) || !File.Exists(stagedInputPath);

            if (originalDeleted && processedDeleted && stagedDeleted)
            {
                return;
            }

            await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {ProcessingPollTimeout.TotalSeconds:N0}s waiting for document {uploadedFile.Id} artifacts to be deleted. UploadPathExists={File.Exists(originalUploadPath)}, ProcessedDirectoryExists={Directory.Exists(processedDocumentDirectory)}, StagedPathExists={!string.IsNullOrWhiteSpace(stagedInputPath) && File.Exists(stagedInputPath)}");
    }

    private async Task<string> GetPythonDocumentVisibilityDiagnosticAsync(HttpClient pythonClient, int documentId)
    {
        async Task<string> QueryAsync(string endpoint)
        {
            try
            {
                using var response = await pythonClient.GetAsync(endpoint, TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                return $"GET '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}' -> {(int)response.StatusCode} {response.ReasonPhrase}. Response: {body}";
            }
            catch (Exception ex)
            {
                return $"GET '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}' threw {ex.GetType().Name}: {ex.Message}";
            }
        }

        var listDiagnostic = await QueryAsync("documents");
        var unprocessedDiagnostic = await QueryAsync("documents/unprocessed");
        var documentDiagnostic = await QueryAsync($"documents/{documentId}");
        var statusDiagnostic = await QueryAsync($"processing/status/{documentId}");
        return $"Python visibility diagnostics:{Environment.NewLine}{listDiagnostic}{Environment.NewLine}{unprocessedDiagnostic}{Environment.NewLine}{documentDiagnostic}{Environment.NewLine}{statusDiagnostic}";
    }

    private async Task<PythonDocumentApiResponse> WaitForPythonDocumentVisibleInListAsync(HttpClient pythonClient, int documentId)
    {
        const string endpoint = "documents";
        var waitStopwatch = Stopwatch.StartNew();
        string lastResult = "<no response received>";

        while (waitStopwatch.Elapsed < PythonVisibilityTimeout)
        {
            try
            {
                using var response = await pythonClient.GetAsync(endpoint, TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                lastResult = $"{(int)response.StatusCode} {response.ReasonPhrase}. Response: {body}";

                if (response.StatusCode == HttpStatusCode.InternalServerError && IsTransientDatabaseFailure(body))
                {
                    await Task.Delay(PythonVisibilityPollInterval, TestContext.Current.CancellationToken);
                    continue;
                }

                Assert.True(response.IsSuccessStatusCode,
                    $"Python document list endpoint '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}' returned {(int)response.StatusCode} {response.ReasonPhrase} while waiting for uploaded document visibility. Response: {body}");

                var documents = DeserializeJson<List<PythonDocumentApiResponse>>(body, $"GET /{endpoint}");
                var document = documents.FirstOrDefault(candidate => candidate.Id == documentId);
                if (document is not null)
                {
                    Assert.False(string.IsNullOrWhiteSpace(document.ProcessingStatus),
                        $"Python document list endpoint returned an empty processing_status for document {documentId}. Payload: {body}");
                    return document;
                }

                await Task.Delay(PythonVisibilityPollInterval, TestContext.Current.CancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastResult = $"{ex.GetType().Name}: {ex.Message}";
                await Task.Delay(PythonVisibilityPollInterval, TestContext.Current.CancellationToken);
            }
        }

        Assert.Fail(
            $"Timed out after {PythonVisibilityTimeout.TotalSeconds:N0}s waiting for uploaded document {documentId} to appear in the Python document list endpoint '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}'. Last result: {lastResult}");

        return default!;
    }

    private async Task<PythonDocumentApiResponse> WaitForPythonDocumentProcessedInListAsync(HttpClient pythonClient, int documentId)
    {
        const string endpoint = "documents";
        var observedStatuses = new List<string>();
        var pollStopwatch = Stopwatch.StartNew();
        string lastPayload = "<no response received>";

        while (pollStopwatch.Elapsed < ProcessingPollTimeout)
        {
            using var response = await pythonClient.GetAsync(endpoint, TestContext.Current.CancellationToken);
            lastPayload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            if (response.StatusCode == HttpStatusCode.InternalServerError && IsTransientDatabaseFailure(lastPayload))
            {
                await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
                continue;
            }

            Assert.True(response.IsSuccessStatusCode,
                $"Python document list endpoint '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}' returned {(int)response.StatusCode} {response.ReasonPhrase} while waiting for processing completion. Response: {lastPayload}");

            var documents = DeserializeJson<List<PythonDocumentApiResponse>>(lastPayload, $"GET /{endpoint}");
            var document = documents.FirstOrDefault(candidate => candidate.Id == documentId);
            if (document is null)
            {
                await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(document.ProcessingStatus),
                $"Python document list endpoint returned an empty processing_status for document {documentId}. Payload: {lastPayload}");

            observedStatuses.Add(document.ProcessingStatus!);

            if (document.ProcessingStatus.Equals("processed", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }

            if (document.ProcessingStatus.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail(
                    $"Python document list reported error status for document {documentId}. Payload: {lastPayload}");
            }

            if (!document.ProcessingStatus.Equals("uploaded", StringComparison.OrdinalIgnoreCase) &&
                !document.ProcessingStatus.Equals("processing", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail(
                    $"Python document list returned unexpected status '{document.ProcessingStatus}' for document {documentId}. Payload: {lastPayload}");
            }

            await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {ProcessingPollTimeout.TotalSeconds:N0}s waiting for document {documentId} to reach 'processed' in the Python document list. Observed statuses: {string.Join(" -> ", observedStatuses)}. Last payload: {lastPayload}");

        return default!;
    }

    private async Task<UploadedFileApiModel> WaitForUploadedFileStatusAsync(HttpClient webClient, int documentId, string expectedStatus, int timeoutMs = 120000)
    {
        var waitStopwatch = Stopwatch.StartNew();
        string lastPayload = "<no upload state returned>";

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
            lastPayload = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(listResponse.IsSuccessStatusCode,
                $"Upload state query '{BuildAbsoluteUri(_data.WebfrontendUri, "api/FileUpload")}' returned {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}. Response: {lastPayload}");

            var listResult = DeserializeJson<UploadedFilesApiResponse>(lastPayload, "GET /api/FileUpload");
            Assert.True(listResult.Success, $"Upload state query returned success=false. Response: {lastPayload}");

            var uploadedFile = listResult.Files.FirstOrDefault(file => file.Id == documentId);
            if (uploadedFile is not null &&
                string.Equals(uploadedFile.Status, expectedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return uploadedFile;
            }

            await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeoutMs}ms waiting for document {documentId} to reach '{expectedStatus}' in the Web upload state API. Last payload: {lastPayload}");
        return default!;
    }

    private async Task<ProcessedArtifactsInfo> WaitForProcessedArtifactsAsync(int documentId)
    {
        Assert.False(string.IsNullOrWhiteSpace(_data.SharedDataPath),
            "Test fixture did not expose the isolated shared data path.");

        var documentDirectory = Path.Combine(_data.SharedDataPath, "processed", "documents", documentId.ToString());
        var documentJsonPath = Path.Combine(documentDirectory, "document.json");
        var metadataPath = Path.Combine(documentDirectory, "metadata.json");
        var pagesDirectory = Path.Combine(documentDirectory, "pages");
        var outputsDirectory = Path.Combine(documentDirectory, "outputs");
        var inputsDirectory = Path.Combine(_data.SharedDataPath, "inputs");
        var pollStopwatch = Stopwatch.StartNew();
        string lastObservation = "<artifacts not observed>";

        while (pollStopwatch.Elapsed < ProcessingPollTimeout)
        {
            var firstPagePath = Directory.Exists(pagesDirectory)
                ? Directory.EnumerateFiles(pagesDirectory, "page_*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                : null;
            var markdownPath = Directory.Exists(outputsDirectory)
                ? Directory.EnumerateFiles(outputsDirectory, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                : null;
            var stagedMarkdownPath = Directory.Exists(inputsDirectory)
                ? Directory.EnumerateFiles(inputsDirectory, $"{documentId:D6}-*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                : null;
            var lightRagHandoff = TryReadLightRagHandoffInfo(metadataPath);

            if (File.Exists(documentJsonPath) &&
                firstPagePath is not null &&
                markdownPath is not null &&
                lightRagHandoff?.ScanRequested == true)
            {
                return new ProcessedArtifactsInfo
                {
                    DocumentJsonPath = documentJsonPath,
                    FirstPagePath = firstPagePath,
                    MarkdownPath = markdownPath,
                    MetadataPath = metadataPath,
                    LightRagScanRequested = true,
                    LightRagStagedInputPath = lightRagHandoff.StagedInputPath ?? string.Empty,
                    ObservedStagedMarkdownPath = stagedMarkdownPath ?? string.Empty
                };
            }

            lastObservation =
                $"document.json={File.Exists(documentJsonPath)}, metadata={DescribeMetadataState(metadataPath, lightRagHandoff)}, firstPage={(firstPagePath is not null ? firstPagePath : "<missing>")}, markdown={(markdownPath is not null ? markdownPath : "<missing>")}, staged={(stagedMarkdownPath is not null ? stagedMarkdownPath : "<missing>")}";

            await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {ProcessingPollTimeout.TotalSeconds:N0}s waiting for processed artifacts for document {documentId} under '{documentDirectory}'. Last observation: {lastObservation}");
        return default!;
    }

    private static async Task WaitForLightRagIngestionAsync(HttpClient lightRagClient, ProcessedArtifactsInfo artifacts, int timeoutMs = 120000)
    {
        var stagedInputFileName = Path.GetFileName(artifacts.LightRagStagedInputPath);
        Assert.False(string.IsNullOrWhiteSpace(stagedInputFileName), "Processed artifacts did not record a LightRAG staged input path.");

        var waitStopwatch = Stopwatch.StartNew();
        string lastDocumentsPayload = "<no LightRAG documents payload returned>";
        string lastPipelinePayload = "<no LightRAG pipeline payload returned>";

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            using var documentsResponse = await lightRagClient.GetAsync("documents", TestContext.Current.CancellationToken);
            lastDocumentsPayload = await documentsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(documentsResponse.IsSuccessStatusCode,
                $"LightRAG documents query returned {(int)documentsResponse.StatusCode} {documentsResponse.ReasonPhrase}. Response: {lastDocumentsPayload}");

            var documentVisible = LightRagDocumentsContainFile(lastDocumentsPayload, stagedInputFileName);

            using var pipelineResponse = await lightRagClient.GetAsync("documents/pipeline_status", TestContext.Current.CancellationToken);
            lastPipelinePayload = await pipelineResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(pipelineResponse.IsSuccessStatusCode,
                $"LightRAG pipeline query returned {(int)pipelineResponse.StatusCode} {pipelineResponse.ReasonPhrase}. Response: {lastPipelinePayload}");

            var pipelineBusy = LightRagPipelineBusy(lastPipelinePayload);
            if (documentVisible && !pipelineBusy)
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeoutMs}ms waiting for LightRAG ingestion of '{stagedInputFileName}'. Last documents payload: {lastDocumentsPayload}{Environment.NewLine}Last pipeline payload: {lastPipelinePayload}");
    }

    private static async Task WaitForLightRagPipelineIdleAsync(HttpClient lightRagClient, int timeoutMs = 120000)
    {
        var waitStopwatch = Stopwatch.StartNew();
        string lastPipelinePayload = "<no LightRAG pipeline payload returned>";

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            using var pipelineResponse = await lightRagClient.GetAsync("documents/pipeline_status", TestContext.Current.CancellationToken);
            lastPipelinePayload = await pipelineResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(pipelineResponse.IsSuccessStatusCode,
                $"LightRAG pipeline query returned {(int)pipelineResponse.StatusCode} {pipelineResponse.ReasonPhrase}. Response: {lastPipelinePayload}");

            if (!LightRagPipelineBusy(lastPipelinePayload))
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeoutMs}ms waiting for the LightRAG pipeline to become idle. Last pipeline payload: {lastPipelinePayload}");
    }

    private static LightRagHandoffInfo? TryReadLightRagHandoffInfo(string metadataPath)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(metadataPath);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("lightrag", out var lightragElement) ||
                lightragElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var scanRequested =
                lightragElement.TryGetProperty("scan_requested", out var scanRequestedElement) &&
                (scanRequestedElement.ValueKind == JsonValueKind.True || scanRequestedElement.ValueKind == JsonValueKind.False) &&
                scanRequestedElement.GetBoolean();

            string? stagedInputPath = null;
            if (lightragElement.TryGetProperty("staged_input_path", out var stagedInputPathElement) &&
                stagedInputPathElement.ValueKind == JsonValueKind.String)
            {
                stagedInputPath = stagedInputPathElement.GetString();
            }

            return new LightRagHandoffInfo
            {
                ScanRequested = scanRequested,
                StagedInputPath = stagedInputPath
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LightRagDocumentsContainFile(string payload, string targetFileName)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("statuses", out var statusesElement) ||
                statusesElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var status in statusesElement.EnumerateObject())
            {
                if (status.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in status.Value.EnumerateArray())
                {
                    if (!entry.TryGetProperty("file_path", out var filePathElement) ||
                        filePathElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(filePathElement.GetString());
                    if (string.Equals(fileName, targetFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool LightRagPipelineBusy(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("busy", out var busyElement) &&
                   busyElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                   busyElement.GetBoolean();
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string DescribeMetadataState(string metadataPath, LightRagHandoffInfo? lightRagHandoff)
    {
        if (!File.Exists(metadataPath))
        {
            return "<missing>";
        }

        if (lightRagHandoff is null)
        {
            return $"{metadataPath} (missing lightrag metadata)";
        }

        var stagedInputPath = string.IsNullOrWhiteSpace(lightRagHandoff.StagedInputPath)
            ? "<missing>"
            : lightRagHandoff.StagedInputPath;

        return $"{metadataPath} (scan_requested={lightRagHandoff.ScanRequested}, staged_input_path={stagedInputPath})";
    }

    private async Task<Neo4jLightRagGraphState> QueryNeo4jLightRagGraphAsync(HttpClient graphDbClient)
    {
        using var response = await graphDbClient.PostAsJsonAsync(
            "db/neo4j/tx/commit",
            new
            {
                statements = new[]
                {
                    new
                    {
                        statement = """
                            MATCH (n)
                            WHERE NOT n:Document AND NOT n:Page
                            OPTIONAL MATCH (n)-[r]-()
                            RETURN count(DISTINCT n) AS nodeCount, count(DISTINCT r) AS relationshipCount
                            """
                    }
                }
            },
            TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode,
            $"Neo4j LightRAG graph query returned {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseBody}");

        using var payload = JsonDocument.Parse(responseBody);
        if (payload.RootElement.TryGetProperty("errors", out var errorsElement) &&
            errorsElement.ValueKind == JsonValueKind.Array &&
            errorsElement.GetArrayLength() > 0)
        {
            Assert.Fail($"Neo4j LightRAG graph query returned errors: {errorsElement}");
        }

        var resultsElement = payload.RootElement.GetProperty("results");
        var row = resultsElement[0].GetProperty("data")[0].GetProperty("row");
        return new Neo4jLightRagGraphState
        {
            NodeCount = row[0].GetInt32(),
            RelationshipCount = row[1].GetInt32()
        };
    }

    private async Task<Neo4jDocumentGraphState> QueryNeo4jDocumentGraphAsync(HttpClient graphDbClient, int documentId)
    {
        using var response = await graphDbClient.PostAsJsonAsync(
            "db/neo4j/tx/commit",
            new
            {
                statements = new[]
                {
                    new
                    {
                        statement = """
                            MATCH (d:Document {id: $documentId})
                            OPTIONAL MATCH (d)-[:CONTAINS]->(p:Page)
                            RETURN count(DISTINCT d) AS documentCount, count(DISTINCT p) AS pageCount
                            """,
                        parameters = new
                        {
                            documentId
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode,
            $"Neo4j transactional query for document {documentId} returned {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseBody}");

        using var payload = JsonDocument.Parse(responseBody);
        if (payload.RootElement.TryGetProperty("errors", out var errorsElement) &&
            errorsElement.ValueKind == JsonValueKind.Array &&
            errorsElement.GetArrayLength() > 0)
        {
            Assert.Fail($"Neo4j transactional query returned errors for document {documentId}: {errorsElement}");
        }

        var resultsElement = payload.RootElement.GetProperty("results");
        var row = resultsElement[0].GetProperty("data")[0].GetProperty("row");
        return new Neo4jDocumentGraphState
        {
            DocumentCount = row[0].GetInt32(),
            PageCount = row[1].GetInt32()
        };
    }

    private async Task<Neo4jLightRagGraphState> WaitForLightRagNeo4jGraphGrowthAsync(
        HttpClient graphDbClient,
        Neo4jLightRagGraphState baselineState,
        int timeoutMs = 120000)
    {
        var waitStopwatch = Stopwatch.StartNew();
        Neo4jLightRagGraphState? latestState = null;

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            latestState = await QueryNeo4jLightRagGraphAsync(graphDbClient);
            if (latestState.NodeCount > baselineState.NodeCount ||
                latestState.RelationshipCount > baselineState.RelationshipCount)
            {
                return latestState;
            }

            await Task.Delay(1000, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeoutMs}ms waiting for LightRAG-managed Neo4j graph state to grow. Baseline nodes/relationships: {baselineState.NodeCount}/{baselineState.RelationshipCount}. Last observed nodes/relationships: {latestState?.NodeCount ?? 0}/{latestState?.RelationshipCount ?? 0}.");
        return default!;
    }

    private async Task<KnowledgeQueryApiResponse> WaitForKnowledgeQueryResultAsync(
        HttpClient client,
        string relativePath,
        object payload,
        int timeoutMs = 120000)
    {
        var waitStopwatch = Stopwatch.StartNew();
        string lastResult = "<no retrieval response received>";

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            using var response = await client.PostAsJsonAsync(relativePath, payload, TestContext.Current.CancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            lastResult = $"{(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseBody}";

            if (response.IsSuccessStatusCode)
            {
                var result = DeserializeJson<KnowledgeQueryApiResponse>(responseBody, $"POST /{relativePath}");
                result.RawJson = responseBody;
                if (result.Results.Any(item => !string.IsNullOrWhiteSpace(item.Content)))
                {
                    return result;
                }
            }
            else if (response.StatusCode is not (HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable))
            {
                Assert.Fail($"Knowledge query '{relativePath}' failed unexpectedly. {lastResult}");
            }

            await Task.Delay(1000, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeoutMs}ms waiting for knowledge query '{relativePath}' to return a non-empty result. Last result: {lastResult}");
        return default!;
    }

    private static bool ContainsSignificantQueryTerm(string content, string queryText)
    {
        foreach (var queryTerm in queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (queryTerm.Length < 5)
            {
                continue;
            }

            if (content.Contains(queryTerm, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<PythonDocumentApiResponse> WaitForPythonDocumentVisibleAsync(HttpClient pythonClient, int documentId)
    {
        var endpoint = $"documents/{documentId}";
        var waitStopwatch = Stopwatch.StartNew();
        string lastResult = "<no response received>";

        while (waitStopwatch.Elapsed < PythonVisibilityTimeout)
        {
            try
            {
                using var response = await pythonClient.GetAsync(endpoint, TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                lastResult = $"{(int)response.StatusCode} {response.ReasonPhrase}. Response: {body}";

                if (response.StatusCode == HttpStatusCode.NotFound ||
                    (response.StatusCode == HttpStatusCode.InternalServerError && IsTransientDatabaseFailure(body)))
                {
                    await Task.Delay(PythonVisibilityPollInterval, TestContext.Current.CancellationToken);
                    continue;
                }

                Assert.True(response.IsSuccessStatusCode,
                    $"Python document endpoint '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}' returned {(int)response.StatusCode} {response.ReasonPhrase} while waiting for uploaded document visibility. Response: {body}");

                var document = DeserializeJson<PythonDocumentApiResponse>(body, $"GET /{endpoint}");
                Assert.Equal(documentId, document.Id);
                Assert.False(string.IsNullOrWhiteSpace(document.ProcessingStatus),
                    $"Python document endpoint returned an empty processing_status for document {documentId}. Payload: {body}");

                return document;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastResult = $"{ex.GetType().Name}: {ex.Message}";
                await Task.Delay(PythonVisibilityPollInterval, TestContext.Current.CancellationToken);
            }
        }

        Assert.Fail(
            $"Timed out after {PythonVisibilityTimeout.TotalSeconds:N0}s waiting for uploaded document {documentId} to become visible through the Python API endpoint '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}'. Last result: {lastResult}");

        return default!;
    }

    private static bool IsTransientDatabaseFailure(string responseBody)
    {
        return responseBody.Contains("unable to open database file", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("disk i/o error", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("disk io error", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ProcessingStatusApiResponse> PollForProcessingCompletionAsync(HttpClient pythonClient, int documentId)
    {
        var endpoint = $"processing/status/{documentId}";
        var observedStatuses = new List<string>();
        var pollStopwatch = Stopwatch.StartNew();
        string lastPayload = "<no response received>";

        while (pollStopwatch.Elapsed < ProcessingPollTimeout)
        {
            try
            {
                using var statusResponse = await pythonClient.GetAsync(endpoint, TestContext.Current.CancellationToken);
                lastPayload = await statusResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

                if (statusResponse.StatusCode == HttpStatusCode.NotFound ||
                    (statusResponse.StatusCode == HttpStatusCode.InternalServerError && IsTransientDatabaseFailure(lastPayload)))
                {
                    await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
                    continue;
                }

                Assert.True(statusResponse.IsSuccessStatusCode,
                    $"Python status endpoint '{BuildAbsoluteUri(_data.PythonServiceUri, endpoint)}' returned {(int)statusResponse.StatusCode} {statusResponse.ReasonPhrase}. Response: {lastPayload}");

                var status = DeserializeJson<ProcessingStatusApiResponse>(lastPayload, $"GET /{endpoint}");
                status.RawJson = lastPayload;

                Assert.Equal(documentId, status.DocumentId);
                Assert.False(string.IsNullOrWhiteSpace(status.Status),
                    $"Python status endpoint returned an empty status for document {documentId}. Payload: {lastPayload}");

                observedStatuses.Add(status.Status);

                if (status.Status.Equals("processed", StringComparison.OrdinalIgnoreCase))
                {
                    return status;
                }

                if (status.Status.Equals("error", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail(
                        $"Python processing reported error for document {documentId}: {status.ErrorMessage ?? "<no error_message>"}. Status payload: {lastPayload}");
                }

                if (!status.Status.Equals("uploaded", StringComparison.OrdinalIgnoreCase) &&
                    !status.Status.Equals("processing", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail(
                        $"Python status endpoint returned unexpected status '{status.Status}' for document {documentId}. Payload: {lastPayload}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastPayload = $"{ex.GetType().Name}: {ex.Message}";
            }

            await Task.Delay(ProcessingPollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"Timed out after {ProcessingPollTimeout.TotalSeconds:N0}s waiting for document {documentId} to reach 'processed'. Observed statuses: {string.Join(" -> ", observedStatuses)}. Last payload: {lastPayload}");

        return default!;
    }

    private static async Task WaitForUploadedFileRowAsync(IPage page, string fileName, int timeoutMs = 15000)
    {
        var waitStopwatch = Stopwatch.StartNew();

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var rows = await GetDocumentSourceTableRows(page);

            foreach (var row in rows)
            {
                var cellText = (await row.Locator(FilenameCellInRow).TextContentAsync())?.Trim();
                if (string.Equals(cellText, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Uploaded file '{fileName}' did not appear in the Upload Documents table within {timeoutMs}ms.");
    }

    private static async Task WaitForUploadedFileRowRemovedAsync(IPage page, string fileName, int timeoutMs = 120000)
    {
        var waitStopwatch = Stopwatch.StartNew();

        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var rows = await GetDocumentSourceTableRows(page);
            var stillPresent = false;

            foreach (var row in rows)
            {
                var cellText = (await row.Locator(FilenameCellInRow).TextContentAsync())?.Trim();
                if (string.Equals(cellText, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    stillPresent = true;
                    break;
                }
            }

            if (!stillPresent)
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Uploaded file '{fileName}' still appeared in the Upload Documents table after deletion.");
    }

    private static async Task<string> WaitForUploadSuccessAsync(IPage page, string filePrefix)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(30);
        var lastAlert = string.Empty;

        while (DateTime.UtcNow < timeoutAt)
        {
            var alert = page.Locator(".alert").First;
            try
            {
                if (await alert.IsVisibleAsync())
                {
                    lastAlert = (await alert.TextContentAsync()) ?? string.Empty;
                    if (lastAlert.Contains("Authentication is required", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.Fail($"Upload surfaced an authentication failure. Alert: {lastAlert}");
                    }

                    if (lastAlert.Contains("Upload Failed", StringComparison.OrdinalIgnoreCase) ||
                        lastAlert.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.Fail($"Upload failed. Alert: {lastAlert}");
                    }
                }
            }
            catch (PlaywrightException)
            {
            }

            var rowText = await TryFindRowTextByPrefixAsync(page, filePrefix);
            if (!string.IsNullOrWhiteSpace(rowText))
            {
                return rowText;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out waiting for an uploaded row with prefix '{filePrefix}'. Last alert: {lastAlert}");
        return string.Empty;
    }

    private static async Task<string?> TryFindRowTextByPrefixAsync(IPage page, string filePrefix)
    {
        var row = await TryFindRowByPrefixAsync(page, filePrefix);
        return row is null ? null : await row.TextContentAsync();
    }

    private static async Task<ILocator?> TryFindRowByPrefixAsync(IPage page, string filePrefix)
    {
        var rows = await GetDocumentSourceTableRows(page);
        foreach (var row in rows)
        {
            var rowText = (await row.TextContentAsync()) ?? string.Empty;
            if (rowText.Contains(filePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    private static T DeserializeJson<T>(string payload, string endpoint)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            Assert.NotNull(result);
            return result!;
        }
        catch (JsonException ex)
        {
            Assert.Fail(
                $"Endpoint '{endpoint}' returned a payload that did not match the expected contract. Error: {ex.Message}{Environment.NewLine}Payload:{Environment.NewLine}{payload}");
            return default!;
        }
    }

    private static string BuildAbsoluteUri(string baseUri, string relativePath)
    {
        return new Uri(new Uri($"{baseUri.TrimEnd('/')}/"), relativePath).AbsoluteUri;
    }

    private static async Task<IReadOnlyList<ILocator>> GetDocumentSourceTableRows(IPage page)
    {
        try
        {
            await WaitForLocator(page.Locator(FileTableRow));
            IReadOnlyList<ILocator> updatedFilenameCells = await page.Locator(FileTableRow).AllAsync();
            return updatedFilenameCells;
        }
        catch (Exception e)
        {
            // return empty list as it appears to be empty
            return Array.Empty<ILocator>();
        }
    }

    private static async Task<ILocator> PullFileDeleteButton(IReadOnlyList<ILocator> filenameRows, string filename)
    {
        foreach (var fileRow in filenameRows)
        {
            var cellText = await fileRow.Locator(FilenameCellInRow).TextContentAsync();
            if (string.IsNullOrWhiteSpace(cellText))
            {
                continue;
            }

            var exactMatch = string.Equals(cellText, filename, StringComparison.OrdinalIgnoreCase);
            var stemMatch = cellText.StartsWith(Path.GetFileNameWithoutExtension(filename), StringComparison.OrdinalIgnoreCase);
            if (!exactMatch && !stemMatch)
            {
                Debug.WriteLine(cellText);
                continue;
            }

            IReadOnlyList<ILocator> deleteButtons = await fileRow.Locator(DeleteButtonInCell).AllAsync();
            var targetFileToDelete = deleteButtons.FirstOrDefault();
            Debug.WriteLine($"Found file ({cellText}) matching {filename}");
            return targetFileToDelete;
        }

        return null;
    }

    private static async Task CheckAtleastOneCellExists(IReadOnlyList<ILocator> filenameRows)
    {
        if (filenameRows.Count() == 0)
        {
            Debug.WriteLine("No file rows found as list is empty");
            throw new InvalidOperationException("No file rows found in the table. It may be that there are no files uploaded or there is an issue with the locator.");
        }
        var cells = await filenameRows[0].Locator(FilenameCellInRow).AllAsync();
        if (cells.Count() == 0)
        {
            // wait and try again as it may be that the file list is still loading.
            await Task.Delay(2000);
            cells = await filenameRows[0].Locator(FilenameCellInRow).AllAsync();
        }
        if (cells.Count() == 0)
        {
            Debug.WriteLine("No file name spans found within the first cell");
            throw new InvalidOperationException("No file name spans found within the first cell. It may be that the table structure has changed or there is an issue with the locator.");
        }
    }

    private static async Task SetUploadInput(string testDocumentPath, AriaRole role, string nameText, IPage page)
    {
        var fileInput = page.Locator("[data-testid='upload-file-input'], input[type='file']").First;
        if (await fileInput.CountAsync() > 0)
        {
            await fileInput.SetInputFilesAsync(testDocumentPath);
            var directInputValue = PullFilename(await fileInput.InputValueAsync());
            if (directInputValue == PullFilename(testDocumentPath))
            {
                return;
            }
        }

        ILocator control = page.GetByRole(role, new() { Name = nameText });
        await WaitForLocator(control);
        var inputValue = PullFilename(await control.InputValueAsync());
        int countDown = 5;
        while (inputValue != PullFilename(testDocumentPath) && countDown > 0)
        {
            countDown--;
            var fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
            {
                await control.HoverAsync();
                await control.ClickAsync();
                await WaitForPageLoadCompletion(page);
            });
            await fileChooser.SetFilesAsync(testDocumentPath, new FileChooserSetFilesOptions { Timeout = 5000 });
            await WaitForPageLoadCompletion(page);
            inputValue = PullFilename(await control.InputValueAsync());
        }
        if (inputValue != PullFilename(testDocumentPath))
        {
            throw new InvalidAsynchronousStateException($"File input value '{inputValue}' does not match expected path '{testDocumentPath}'.");
        }
    }

    private static async Task WaitForUploadButtonEnabledAsync(ILocator uploadButton)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (!await uploadButton.IsDisabledAsync())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Upload button stayed disabled after a file was selected.");
    }

    private static string PullFilename(string filePathName)
    {
        // pull only the filename from the full path.
        if (string.IsNullOrWhiteSpace(filePathName))
        {
            return string.Empty;
        }
        // Handle both Windows and Unix-style paths
        char[] separators = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return filePathName.Split(separators, StringSplitOptions.RemoveEmptyEntries).Last();
    }

    private static async Task ClickByRole(AriaRole role, string name, IPage page)
    {
        var target = page.GetByRole(role, new() { Name = name }).First;
        var closeDesktopSidebarAfterClick = await EnsureNavigationTargetVisibleAsync(target, page);
        await target.ClickAsync(new LocatorClickOptions() { Delay = 250, Force = true });
        await WaitForPageLoadCompletion(page);

        if (closeDesktopSidebarAfterClick)
        {
            await EnsureDesktopSidebarClosedAsync(page);
        }
    }

    private static async Task<bool> EnsureNavigationTargetVisibleAsync(ILocator target, IPage page)
    {
        await WaitForLocator(target, 30_000);

        if (await IsWithinViewportAsync(target))
        {
            return false;
        }

        var desktopToggle = page.Locator(".sidebar-toggle").First;
        if (await desktopToggle.IsVisibleAsync())
        {
            var openedDesktopSidebar = false;
            if (!await IsDesktopSidebarOpenAsync(page))
            {
                await desktopToggle.ClickAsync(new LocatorClickOptions { Delay = 100, Force = true });
                openedDesktopSidebar = true;
            }

            await WaitForNavigationTargetWithinViewportAsync(target);
            return openedDesktopSidebar;
        }

        var mobileToggle = page.Locator(".navbar-toggler").First;
        if (await mobileToggle.IsVisibleAsync())
        {
            if (!await mobileToggle.IsCheckedAsync())
            {
                await mobileToggle.ClickAsync(new LocatorClickOptions { Delay = 100, Force = true });
            }

            await WaitForNavigationTargetWithinViewportAsync(target);
            return false;
        }

        await WaitForNavigationTargetWithinViewportAsync(target);
        return false;
    }

    private static async Task EnsureDesktopSidebarClosedAsync(IPage page)
    {
        if (!await IsDesktopSidebarOpenAsync(page))
        {
            return;
        }

        var backdrop = page.Locator(".sidebar-backdrop").First;
        if (await backdrop.IsVisibleAsync())
        {
            await backdrop.ClickAsync(new LocatorClickOptions { Delay = 100, Force = true });
        }
        else
        {
            var desktopToggle = page.Locator(".sidebar-toggle").First;
            if (!await desktopToggle.IsVisibleAsync())
            {
                return;
            }

            await desktopToggle.ClickAsync(new LocatorClickOptions { Delay = 100, Force = true });
        }

        await page.WaitForFunctionAsync(
            "() => document.querySelector('.page')?.classList.contains('sidebar-open') === false",
            new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    private static async Task<bool> IsDesktopSidebarOpenAsync(IPage page)
    {
        return await page.Locator(".page").First.EvaluateAsync<bool>(
            "element => element.classList.contains('sidebar-open')");
    }

    private static async Task<bool> IsWithinViewportAsync(ILocator target)
    {
        return await target.EvaluateAsync<bool>(
            @"element => {
                const rect = element.getBoundingClientRect();
                const centerX = rect.left + (rect.width / 2);
                const centerY = rect.top + (rect.height / 2);
                return rect.width > 0 &&
                    rect.height > 0 &&
                    centerX >= 0 &&
                    centerX <= window.innerWidth &&
                    centerY >= 0 &&
                    centerY <= window.innerHeight;
            }");
    }

    private static async Task WaitForNavigationTargetWithinViewportAsync(ILocator target, int timeoutMs = 30_000)
    {
        await WaitForLocator(target, timeoutMs);

        var waitStopwatch = Stopwatch.StartNew();
        while (waitStopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (await IsWithinViewportAsync(target))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Navigation target '{await target.TextContentAsync()}' did not become clickable within {timeoutMs}ms.");
    }

    private static async Task WaitForLocator(ILocator locator, int timeout = 5000)
    {
        await locator.First.WaitForAsync(new LocatorWaitForOptions()
        {
            Timeout = timeout,
            State = WaitForSelectorState.Visible
        });
    }

    private static async Task WaitForPageLoadCompletion(IPage page)
    {
        // Wait for the page to reach a stable state after navigation or interaction
        await Task.WhenAll(
            page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 10000 }),
            page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 }),
            page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10000 }),
            page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 10000 })
        );
    }

    private sealed class UploadedFilesApiResponse
    {
        public bool Success { get; set; }
        public List<UploadedFileApiModel> Files { get; set; } = [];
    }

    private sealed class UploadedFileApiModel
    {
        public int Id { get; set; }
        public string? FileName { get; set; }
        public string? OriginalFileName { get; set; }
        public string? SourceType { get; set; }
        public string? Status { get; set; }
    }

    private sealed record AuthenticatedClient(HttpClient Client, string TenantId);

    private sealed class FileUploadApiResponse
    {
        public bool Success { get; set; }
        public bool IsDuplicate { get; set; }
    }

    private sealed class ProcessingTriggerResponse
    {
        public string? Message { get; set; }
    }

    private sealed class PythonDocumentApiResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("filename")]
        public string? FileName { get; set; }

        [JsonPropertyName("original_filename")]
        public string? OriginalFilename { get; set; }

        [JsonPropertyName("processing_status")]
        public string? ProcessingStatus { get; set; }
    }

    private sealed class ProcessingStatusApiResponse
    {
        [JsonPropertyName("document_id")]
        public int DocumentId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("total_pages")]
        public int? TotalPages { get; set; }

        [JsonPropertyName("processed_pages")]
        public int? ProcessedPages { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime? StartedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [JsonIgnore]
        public string RawJson { get; set; } = string.Empty;
    }

    private sealed class ProcessedArtifactsInfo
    {
        public string DocumentJsonPath { get; set; } = string.Empty;
        public string FirstPagePath { get; set; } = string.Empty;
        public string MarkdownPath { get; set; } = string.Empty;
        public string MetadataPath { get; set; } = string.Empty;
        public bool LightRagScanRequested { get; set; }
        public string LightRagStagedInputPath { get; set; } = string.Empty;
        public string ObservedStagedMarkdownPath { get; set; } = string.Empty;
    }

    private sealed class LightRagHandoffInfo
    {
        public bool ScanRequested { get; set; }
        public string? StagedInputPath { get; set; }
    }

    private sealed class KnowledgeQueryApiResponse
    {
        [JsonPropertyName("tenant_id")]
        public string TenantId { get; set; } = string.Empty;

        [JsonPropertyName("correlation_id")]
        public string CorrelationId { get; set; } = string.Empty;

        [JsonPropertyName("results")]
        public List<KnowledgeQueryItemApiResponse> Results { get; set; } = [];

        [JsonIgnore]
        public string RawJson { get; set; } = string.Empty;
    }

    private sealed class KnowledgeQueryItemApiResponse
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("source_refs")]
        public List<string> SourceRefs { get; set; } = [];

        [JsonPropertyName("relevance_score")]
        public double RelevanceScore { get; set; }
    }

    private sealed class Neo4jDocumentGraphState
    {
        public int DocumentCount { get; set; }

        public int PageCount { get; set; }
    }

    private sealed class Neo4jLightRagGraphState
    {
        public int NodeCount { get; set; }

        public int RelationshipCount { get; set; }
    }
}
