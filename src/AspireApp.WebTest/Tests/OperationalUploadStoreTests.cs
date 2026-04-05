using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Npgsql;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit.v3.Priority;

namespace AspireApp.WebTest.Tests;

[TestCaseOrderer(typeof(PriorityOrderer))]
public sealed class OperationalUploadStoreTests(TestFixture fixture) : IClassFixture<TestFixture>
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppHostMappingModel _mapping = fixture.AppHostMapping;

    [Fact, Priority(1)]
    public async Task UploadApiPersistsMetadataToPostgres()
    {
        Assert.False(string.IsNullOrWhiteSpace(_mapping.UploadStoreConnectionString));

        using var webClient = CreateWebFrontendHttpClient();
        await DeleteExistingTestUploadsAsync(webClient);

        await using var fileStream = File.OpenRead(TestFile);
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(streamContent, "file", Path.GetFileName(TestFile));

        using var uploadResponse = await webClient.PostAsync("api/FileUpload", form, TestContext.Current.CancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(uploadResponse.IsSuccessStatusCode,
            $"Upload API returned {(int)uploadResponse.StatusCode} {uploadResponse.ReasonPhrase}. Response: {uploadBody}");

        var uploadResult = JsonSerializer.Deserialize<FileUploadApiResponse>(uploadBody, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize upload response: {uploadBody}");

        Assert.True(uploadResult.Success, $"Upload API returned success=false. Response: {uploadBody}");
        Assert.False(uploadResult.IsDuplicate, $"Expected a fresh upload in the Postgres cutover test. Response: {uploadBody}");
        Assert.True(uploadResult.Id > 0, $"Upload API did not return a persisted file id. Response: {uploadBody}");
        Assert.Equal("uploaded", uploadResult.Status);
        Assert.False(string.IsNullOrWhiteSpace(uploadResult.FileName));

        await using var connection = new NpgsqlConnection(_mapping.UploadStoreConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("""
            SELECT file_name, original_file_name, status, file_size, source_type, tenant_id
            FROM files
            WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("id", uploadResult.Id);

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken),
            $"Upload row {uploadResult.Id} was not found in the PostgreSQL upload store.");

        Assert.Equal(uploadResult.FileName, reader.GetString(0));
        Assert.Equal(Path.GetFileName(TestFile), reader.GetString(1));
        Assert.Equal("uploaded", reader.GetString(2));
        Assert.Equal(new FileInfo(TestFile).Length, reader.GetInt64(3));
        Assert.Equal("upload", reader.GetString(4));
        Assert.Equal("default", reader.GetString(5));

        await reader.CloseAsync();

        await using var pagesCommand = new NpgsqlCommand("SELECT COUNT(*) FROM document_pages WHERE file_id = @id", connection);
        pagesCommand.Parameters.AddWithValue("id", uploadResult.Id);
        var pageCount = (long)(await pagesCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0L);
        Assert.Equal(0L, pageCount);

        var uploadedFilePath = Path.Combine(_mapping.SharedDataPath, uploadResult.FileName!);
        Assert.True(File.Exists(uploadedFilePath),
            $"Uploaded file was not stored on disk at '{uploadedFilePath}'.");

        using var deleteResponse = await webClient.DeleteAsync($"api/FileUpload/{uploadResult.Id}", TestContext.Current.CancellationToken);
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(deleteResponse.IsSuccessStatusCode,
            $"Cleanup delete for uploaded test row {uploadResult.Id} failed. Response: {deleteBody}");
    }

    private HttpClient CreateWebFrontendHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"{_mapping.WebfrontendUri.TrimEnd('/')}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private async Task DeleteExistingTestUploadsAsync(HttpClient webClient)
    {
        using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
        var listBody = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(listResponse.IsSuccessStatusCode,
            $"Could not query existing uploads before Postgres verification. Response: {listBody}");

        var uploads = JsonSerializer.Deserialize<UploadedFilesApiResponse>(listBody, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize upload list response: {listBody}");

        var originalFileName = Path.GetFileName(TestFile);
        var generatedPrefix = Path.GetFileNameWithoutExtension(originalFileName);

        foreach (var file in uploads.Files.Where(file =>
                     string.Equals(file.SourceType, "upload", StringComparison.OrdinalIgnoreCase) &&
                     (string.Equals(file.OriginalFileName, originalFileName, StringComparison.OrdinalIgnoreCase) ||
                      (!string.IsNullOrWhiteSpace(file.FileName) &&
                       file.FileName.StartsWith(generatedPrefix, StringComparison.OrdinalIgnoreCase)))))
        {
            using var deleteResponse = await webClient.DeleteAsync($"api/FileUpload/{file.Id}", TestContext.Current.CancellationToken);
            var deleteBody = await deleteResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(deleteResponse.IsSuccessStatusCode,
                $"Pre-test cleanup failed for upload {file.Id}. Response: {deleteBody}");
        }
    }

    private sealed class FileUploadApiResponse
    {
        public bool Success { get; set; }
        public bool IsDuplicate { get; set; }
        public int Id { get; set; }
        public string? FileName { get; set; }
        public string? Status { get; set; }
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
    }

    // ==================== TENANT CONTEXT TEST SCAFFOLDING ====================
    // 
    // These tests are commented out because tenant context is not yet implemented.
    // When Jeff adds tenant_id schema + UI selection + service propagation, uncomment
    // and update these tests to match the actual implementation contract.
    //
    // Required implementation before uncommenting:
    // 1. Add tenant_id column to files table (NOT NULL with default or similar)
    // 2. Add tenant selector UI component (NavMenu or Upload page)
    // 3. Update FileStorageService.AddFileAsync to accept tenant_id parameter
    // 4. Update FileUploadController to receive and pass tenant context
    // 5. Add WHERE tenant_id = @tenantId to FileStorageService queries
    // =========================================================================

    /*
    [Fact, Priority(2)]
    public async Task UploadWithTenantIdPersistsTenantContext()
    {
        // Arrange: Upload file with tenant "tenant-A"
        using var webClient = CreateWebFrontendHttpClient();
        await DeleteExistingTestUploadsAsync(webClient);

        await using var fileStream = File.OpenRead(TestFile);
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(streamContent, "file", Path.GetFileName(TestFile));
        
        // TODO: Add tenant_id to form when API contract exists
        // form.Add(new StringContent("tenant-A"), "tenant_id");

        // Act
        using var uploadResponse = await webClient.PostAsync("api/FileUpload", form, TestContext.Current.CancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var uploadResult = JsonSerializer.Deserialize<FileUploadApiResponse>(uploadBody, JsonOptions)!;

        // Assert: Verify tenant_id persisted to Postgres
        await using var connection = new NpgsqlConnection(_mapping.UploadStoreConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT tenant_id FROM files WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", uploadResult.Id);

        var tenantId = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        Assert.Equal("tenant-A", tenantId);

        // Cleanup
        using var deleteResponse = await webClient.DeleteAsync($"api/FileUpload/{uploadResult.Id}", TestContext.Current.CancellationToken);
        Assert.True(deleteResponse.IsSuccessStatusCode);
    }

    [Fact, Priority(3)]
    public async Task GetAllFilesReturnsOnlyCurrentTenantFiles()
    {
        // Arrange: Upload one file per tenant
        using var webClient = CreateWebFrontendHttpClient();
        await DeleteExistingTestUploadsAsync(webClient);

        // TODO: Implement when tenant selection API exists
        // var tenantAFile = await UploadFileWithTenant(webClient, "tenant-A");
        // var tenantBFile = await UploadFileWithTenant(webClient, "tenant-B");

        // Act: Query files as tenant-A
        // TODO: Add tenant context header/cookie when implemented
        using var listResponse = await webClient.GetAsync("api/FileUpload", TestContext.Current.CancellationToken);
        var listBody = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var uploads = JsonSerializer.Deserialize<UploadedFilesApiResponse>(listBody, JsonOptions)!;

        // Assert: Only tenant-A files returned
        // Assert.Single(uploads.Files);
        // Assert.Equal(tenantAFile.Id, uploads.Files[0].Id);

        // TODO: Switch to tenant-B context and verify isolation
    }

    [Fact, Priority(4)]
    public async Task DuplicateDetectionScopedToTenant()
    {
        // Arrange: Upload same file to two different tenants
        using var webClient = CreateWebFrontendHttpClient();
        await DeleteExistingTestUploadsAsync(webClient);

        // TODO: Implement when tenant context exists
        // Act: Upload to tenant-A
        // var tenantAUpload = await UploadFileWithTenant(webClient, "tenant-A");
        // Assert.False(tenantAUpload.IsDuplicate);

        // Act: Upload same file to tenant-B (should NOT be duplicate)
        // var tenantBUpload = await UploadFileWithTenant(webClient, "tenant-B");
        // Assert.False(tenantBUpload.IsDuplicate);

        // Assert: Both uploads succeeded with different IDs
        // Assert.NotEqual(tenantAUpload.Id, tenantBUpload.Id);

        // Act: Upload same file to tenant-A again (should BE duplicate)
        // var tenantADuplicate = await UploadFileWithTenant(webClient, "tenant-A");
        // Assert.True(tenantADuplicate.IsDuplicate);
        // Assert.Equal(tenantAUpload.Id, tenantADuplicate.ExistingFileId);
    }
    */
}
