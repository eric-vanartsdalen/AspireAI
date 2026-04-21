using System.Net;

namespace AspireApp.Web.Services;

public interface IDocumentProcessingCoordinator
{
    Task<AutomaticProcessingDispatchResult> TryStartProcessingAsync(int documentId, CancellationToken cancellationToken = default);

    Task CleanupDocumentAsync(int documentId, CancellationToken cancellationToken = default);
}

public readonly record struct AutomaticProcessingDispatchResult(bool Attempted, bool Started, string? Detail)
{
    public static AutomaticProcessingDispatchResult NotAttempted() => new(false, false, null);
}

public sealed class DocumentProcessingCoordinator(HttpClient httpClient, ILogger<DocumentProcessingCoordinator> logger) : IDocumentProcessingCoordinator
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<DocumentProcessingCoordinator> _logger = logger;

    public async Task<AutomaticProcessingDispatchResult> TryStartProcessingAsync(int documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync(
                $"processing/process-document/{documentId}",
                content: null,
                cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new AutomaticProcessingDispatchResult(true, true, responseBody);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.LogInformation(
                    "Automatic processing was already queued or completed for document {DocumentId}. Response: {ResponseBody}",
                    documentId,
                    responseBody);
                return new AutomaticProcessingDispatchResult(true, true, responseBody);
            }

            _logger.LogWarning(
                "Automatic processing request failed for document {DocumentId}. Status: {StatusCode}. Response: {ResponseBody}",
                documentId,
                (int)response.StatusCode,
                responseBody);
            return new AutomaticProcessingDispatchResult(true, false, responseBody);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Automatic processing request timed out for document {DocumentId}", documentId);
            return new AutomaticProcessingDispatchResult(
                true,
                false,
                "Timed out while asking the processing service to queue this document.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic processing request threw for document {DocumentId}", documentId);
            return new AutomaticProcessingDispatchResult(true, false, ex.Message);
        }
    }

    public async Task CleanupDocumentAsync(int documentId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"processing/cleanup-document/{documentId}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Document cleanup failed for {documentId}. Python service returned {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
    }
}
