using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.Playwright;
using Npgsql;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.v3.Priority;

namespace AspireApp.WebTest.Tests;

[TestCaseOrderer(typeof(PriorityOrderer))]
public sealed class AuthenticatedUploadUxTests : IClassFixture<TestFixture>
{
    private static readonly string TestFile = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "AspireApp.WebTest",
        "DataExample",
        "increase_green_energy_one_rooftop_at_a_time.pdf");

    private const string MockProviderId = "demo";
    private const string MockUserId = "demo-taylor-jones";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppHostMappingModel _mapping;
    private readonly IBrowser _browser;

    public AuthenticatedUploadUxTests(TestFixture fixture)
    {
        _mapping = fixture.AppHostMapping;
        Assert.NotNull(_mapping.Browser);
        _browser = _mapping.Browser!;
    }

    [Fact, Priority(2)]
    public async Task SignedInTenantScopedUserCanUploadDocumentWithoutAuthenticationError()
    {
        var filePrefix = Path.GetFileNameWithoutExtension(TestFile);
        var clientInfo = await CreateWebFrontendHttpClientAsync();
        using var webClient = clientInfo.Client;
        var tenantId = clientInfo.TenantId;

        await DeleteExistingTestUploadsByPrefixAsync(webClient, filePrefix);

        await WithPageAsync(async page =>
        {
            await SignInToUploadPageAsync(page);

            var tenantSelector = page.Locator("#tenant-select");
            Assert.True(await tenantSelector.IsVisibleAsync(), "Signed-in upload flow must keep the tenant selector visible.");

            var resolvedTenantId = await tenantSelector.InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(resolvedTenantId), "Signed-in upload flow did not resolve a tenant.");
            Assert.Equal(tenantId, resolvedTenantId);

            var fileInput = page.Locator("[data-testid='upload-file-input'], input[type='file']").First;
            await fileInput.SetInputFilesAsync(TestFile);

            var uploadButton = page.GetByRole(AriaRole.Button, new() { Name = "Start Upload" });
            await uploadButton.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000
            });
            await WaitForUploadButtonEnabledAsync(uploadButton);

            await uploadButton.ClickAsync();

            var uploadedRow = await WaitForUploadSuccessAsync(page, filePrefix);
            Assert.Contains(filePrefix, uploadedRow, StringComparison.OrdinalIgnoreCase);

            // CRITICAL: Verify the upload persisted to the backend via the API
            var uploadedFile = await WaitForUploadedFileByPrefixAsync(webClient, filePrefix);
            Assert.NotNull(uploadedFile);
            Assert.True(uploadedFile.Id > 0, "Upload succeeded in UI but did not persist a valid file ID to the backend.");
            Assert.False(string.IsNullOrWhiteSpace(uploadedFile.FileName), "Upload succeeded in UI but backend file name is missing.");
            Assert.Contains(filePrefix, uploadedFile.FileName, StringComparison.OrdinalIgnoreCase);
            // After Jeff's fire-and-forget change, status transitions immediately to "processing"
            Assert.True(uploadedFile.Status == "uploaded" || uploadedFile.Status == "processing", 
                $"Expected status 'uploaded' or 'processing', got '{uploadedFile.Status}'");
            Assert.Equal(tenantId, uploadedFile.TenantId);

            await DeleteUploadsByPrefixAsync(page, filePrefix);
        });

        await DeleteExistingTestUploadsByPrefixAsync(webClient, filePrefix);
    }

    private async Task SignInToUploadPageAsync(IPage page)
    {
        var signInPath =
            $"auth/mock/signin?providerId={Uri.EscapeDataString(MockProviderId)}&userId={Uri.EscapeDataString(MockUserId)}&returnUrl=%2Fupload";
        await page.GotoAsync(BuildAbsoluteUri(_mapping.WebfrontendUri, signInPath), _mapping.Options);
        await WaitForPageLoadCompletion(page);

        var uploadSurface = page.Locator("#tenant-select, [data-testid='upload-file-input']");
        await uploadSurface.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });
    }

    private static async Task<string> WaitForUploadSuccessAsync(IPage page, string filePrefix)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(20);
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
                        Assert.Fail($"Upload surfaced an authentication failure for a signed-in tenant user. Alert: {lastAlert}");
                    }

                    if (lastAlert.Contains("Upload Failed", StringComparison.OrdinalIgnoreCase) ||
                        lastAlert.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.Fail($"Upload failed for a signed-in tenant user. Alert: {lastAlert}");
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

    private static async Task DeleteUploadsByPrefixAsync(IPage page, string filePrefix)
    {
        for (var attemptsRemaining = 10; attemptsRemaining > 0; attemptsRemaining--)
        {
            var row = await TryFindRowByPrefixAsync(page, filePrefix);
            if (row is null)
            {
                return;
            }

            var deleteButton = row.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("delete", RegexOptions.IgnoreCase) }).First;
            await deleteButton.ClickAsync();
            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Could not remove all uploaded rows with prefix '{filePrefix}' from the upload page.");
    }

    private static async Task<string?> TryFindRowTextByPrefixAsync(IPage page, string filePrefix)
    {
        var row = await TryFindRowByPrefixAsync(page, filePrefix);
        return row is null ? null : await row.InnerTextAsync();
    }

    private static async Task<ILocator?> TryFindRowByPrefixAsync(IPage page, string filePrefix)
    {
        var rows = await page.Locator("table.file-table tbody tr").AllAsync();
        foreach (var row in rows)
        {
            var rowText = await row.InnerTextAsync();
            if (rowText.Contains(filePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
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

    private static string BuildAbsoluteUri(string baseUri, string relativePath)
    {
        return new Uri(new Uri($"{baseUri.TrimEnd('/')}/"), relativePath).AbsoluteUri;
    }

    private static async Task WaitForPageLoadCompletion(IPage page)
    {
        await Task.WhenAll(
            page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 10_000 }),
            page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10_000 }));
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
            BaseAddress = new Uri($"{_mapping.WebfrontendUri.TrimEnd('/')}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        await AuthenticateAsync(client);
        var tenantId = await ResolveDefaultTenantIdAsync();
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

        return new AuthenticatedClient(client, tenantId);
    }

    private async Task AuthenticateAsync(HttpClient webClient)
    {
        var signInUri = $"auth/mock/signin?providerId={Uri.EscapeDataString(MockProviderId)}&userId={Uri.EscapeDataString(MockUserId)}&returnUrl=%2F";
        using var response = await webClient.GetAsync(signInUri, TestContext.Current.CancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Found)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException($"Mock sign-in failed with {(int)response.StatusCode}. Response: {body}");
        }
    }

    private async Task<string> ResolveDefaultTenantIdAsync()
    {
        await using var connection = new NpgsqlConnection(_mapping.UploadStoreConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("""
            SELECT tenant_id
            FROM tenant_memberships
            WHERE user_id = @userId AND is_default = TRUE
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("userId", MockUserId);

        var tenantId = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("Mock sign-in did not create a default tenant membership.");
        }

        return tenantId;
    }

    private async Task DeleteExistingTestUploadsByPrefixAsync(HttpClient webClient, string filePrefix)
    {
        using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
        var listBody = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(listResponse.IsSuccessStatusCode,
            $"Could not query existing uploads. Response: {listBody}");

        var uploads = JsonSerializer.Deserialize<UploadedFilesApiResponse>(listBody, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize upload list response: {listBody}");

        foreach (var file in uploads.Files.Where(file =>
                     !string.IsNullOrWhiteSpace(file.FileName) &&
                     file.FileName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            using var deleteResponse = await webClient.DeleteAsync($"api/FileUpload/{file.Id}", TestContext.Current.CancellationToken);
            var deleteBody = await deleteResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(deleteResponse.IsSuccessStatusCode,
                $"Pre-test cleanup failed for upload {file.Id}. Response: {deleteBody}");
        }
    }

    private async Task<UploadedFileApiModel?> WaitForUploadedFileByPrefixAsync(HttpClient webClient, string filePrefix, int timeoutMs = 30000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string lastPayload = "<no upload state returned>";

        while (DateTime.UtcNow < deadline)
        {
            using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
            lastPayload = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(listResponse.IsSuccessStatusCode,
                $"Upload state query returned {(int)listResponse.StatusCode}. Response: {lastPayload}");

            var listResult = JsonSerializer.Deserialize<UploadedFilesApiResponse>(lastPayload, JsonOptions);
            Assert.NotNull(listResult);
            Assert.True(listResult.Success, $"Upload state query returned success=false. Response: {lastPayload}");

            var uploadedFile = listResult.Files
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
        return null;
    }

    private sealed record AuthenticatedClient(HttpClient Client, string TenantId);

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
        public string? Status { get; set; }
        public string? TenantId { get; set; }
    }
}
