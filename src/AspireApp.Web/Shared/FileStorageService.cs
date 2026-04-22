using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using AspireApp.Web.Data;
using AspireApp.Web.Services;

namespace AspireApp.Web.Shared;

public class FileStorageService(
    UploadDbContext context,
    ILogger<FileStorageService> logger,
    string dataDirectory,
    IDocumentProcessingCoordinator? documentProcessingCoordinator = null)
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly UploadDbContext _context = context;
    private readonly ILogger<FileStorageService> _logger = logger;
    private readonly string _dataDirectory = dataDirectory;
    private readonly IDocumentProcessingCoordinator? _documentProcessingCoordinator = documentProcessingCoordinator;

    public string DataDirectory => _dataDirectory;

    /// <summary>
    /// Ensures the database and data directory are properly initialized
    /// </summary>
    public async Task<bool> EnsureInitializedAsync()
    {
        try
        {
            // Ensure data directory exists
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Created data directory: {DataDirectory}", _dataDirectory);
                }
            }

            // Ensure database can be accessed
            var canConnect = await _context.Database.CanConnectAsync();
            if (!canConnect)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError("Cannot connect to database");
                }
                return false;
            }

            // Ensure database schema is created (EF Core handles this)
            await _context.Database.EnsureCreatedAsync();
            await EnsureFileSchemaAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Database and directory initialized successfully");
            }
            return true;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error ensuring database and directory initialization");
            }
            return false;
        }
    }

    /// <summary>
    /// Calculates SHA256 hash of file content
    /// </summary>
    public static string CalculateFileHash(Stream fileStream)
    {
        var hashBytes = SHA256.HashData(fileStream);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Calculates SHA256 hash of file content from file path
    /// </summary>
    public static async Task<string> CalculateFileHashAsync(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        var hashBytes = await Task.Run(() => SHA256.HashData(fileStream));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Calculates SHA256 hash of a URL string for duplicate detection
    /// </summary>
    public static string CalculateUrlHash(string url)
    {
        var urlBytes = Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant());
        var hashBytes = SHA256.HashData(urlBytes);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Checks if a file with the same hash already exists
    /// </summary>
    public async Task<FileMetadata?> FindDuplicateByHashAsync(string fileHash, string? tenantId = null)
    {
        try
        {
            await EnsureInitializedAsync();
            var query = _context.Datasources
                .Where(f => f.FileHash == fileHash);

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(f => f.TenantId == tenantId);
            }

            return await query.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error checking for duplicate files with hash: {FileHash}", fileHash);
            }
            return null;
        }
    }

    /// <summary>
    /// Adds a file with hash calculation and duplicate detection
    /// </summary>
    public async Task<FileMetadata> AddFileAsync(string fileName, string originalFilename, string fileDirectory, long size, string fileHash, string status = "uploaded", string tenantId = "default")
    {
        try
        {
            // Ensure database is initialized before adding files
            await EnsureInitializedAsync();

            var fileMetadata = new FileMetadata
            {
                FileName = fileName,
                OriginalFileName = originalFilename,
                FilePath = fileDirectory,
                FileSize = size,
                UploadedAt = DateTime.UtcNow,
                Status = status,
                FileHash = fileHash,
                SourceType = "upload",
                TenantId = tenantId
            };

            _context.Datasources.Add(fileMetadata);
            await _context.SaveChangesAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Added file metadata to database: {FileName}, Size: {Size}, Hash: {Hash}, Status: {Status}, Tenant: {TenantId}",
                    fileName, size, fileHash, status, tenantId);
            }

            return fileMetadata;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error adding file metadata to database");
            }
            throw;
        }
    }

    /// <summary>
    /// Legacy method for backward compatibility - adds file without hash
    /// </summary>
    public async Task<FileMetadata> AddFileAsync(string fileName, string originalFilename, string path, long size)
    {
        return await AddFileAsync(fileName, originalFilename, path, size, string.Empty, "uploaded");
    }

    /// <summary>
    /// Updates file status
    /// </summary>
    public async Task<bool> UpdateFileStatusAsync(int fileId, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync();

            var file = await _context.Datasources.SingleOrDefaultAsync(
                candidate => candidate.Id == fileId,
                cancellationToken);
            if (file == null)
            {
                return false;
            }

            ApplyFileStatus(file, status);

            await _context.SaveChangesAsync(cancellationToken);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Updated file status: {FileId}, Status: {Status}", fileId, file.Status);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error updating file status for file ID: {FileId}", fileId);
            }
            throw;
        }
    }

    public async Task<AutomaticProcessingDispatchResult> RefreshWebSourceAsync(
        int fileId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var file = await BuildTenantScopedQuery(fileId, tenantId).SingleOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            throw new InvalidOperationException("The selected URL source could not be found.");
        }

        if (!UrlSourceTypeClassifier.IsWebSourceType(file.SourceType))
        {
            throw new InvalidOperationException("Refresh is only available for URL-backed sources.");
        }

        if (string.Equals(file.Status, "processing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This URL source is already processing.");
        }

        if (_documentProcessingCoordinator is not null && RequiresExternalCleanup(file))
        {
            await _documentProcessingCoordinator.CleanupDocumentAsync(file.Id, cancellationToken);
        }

        ApplyFileStatus(file, "uploaded");
        await _context.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Reset URL-backed datasource {FileId} ({SourceType}) to uploaded before refresh",
                file.Id,
                file.SourceType);
        }

        return await TryStartAutomaticProcessingAsync(file.Id, cancellationToken);
    }

    /// <summary>
    /// Updates file hash for existing file
    /// </summary>
    public async Task<bool> UpdateFileHashAsync(int fileId, string fileHash)
    {
        try
        {
            await EnsureInitializedAsync();

            var file = await _context.Datasources.FindAsync(fileId);
            if (file == null)
            {
                return false;
            }

            file.FileHash = fileHash;
            await _context.SaveChangesAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Updated file hash: {FileId}, Hash: {Hash}", fileId, fileHash);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error updating file hash for file ID: {FileId}", fileId);
            }
            throw;
        }
    }

    /// <summary>
    /// Gets all files for the specified tenant, or all files if no tenant specified.
    /// </summary>
    public async Task<List<FileMetadata>> GetAllFilesAsync(string? tenantId = null)
    {
        try
        {
            // Ensure database is initialized before querying
            var initialized = await EnsureInitializedAsync();
            if (!initialized)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Database initialization failed, returning empty list");
                }
                return [];
            }

            var query = _context.Datasources.AsQueryable();
            
            // Filter by tenant if specified
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(f => f.TenantId == tenantId);
            }

            return await query.OrderByDescending(f => f.UploadedAt).ToListAsync();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error retrieving files from database");
            }
            // Return empty list instead of throwing to allow UI to function
            return [];
        }
    }

    public async Task<bool> DeleteFileAsync(int id, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure database is initialized before deleting
            await EnsureInitializedAsync();

            var query = _context.Datasources
                .Where(file => file.Id == id);

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(file => file.TenantId == tenantId);
            }

            var file = await query.SingleOrDefaultAsync();
            if (file == null)
            {
                return false;
            }

            if (_documentProcessingCoordinator is not null && RequiresExternalCleanup(file))
            {
                await _documentProcessingCoordinator.CleanupDocumentAsync(file.Id, cancellationToken);
            }

            var fileName = file.FileName;
            var filePath = Path.Combine(_dataDirectory, fileName);

            // EF Core will cascade delete related datasource_pages records
            _context.Datasources.Remove(file);
            await _context.SaveChangesAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Deleted file metadata from database: {FileName}", fileName);
            }

            // Delete the physical file if it exists (only for uploaded files, not URLs)
            if (file.SourceType == "upload" && File.Exists(filePath))
            {
                File.Delete(filePath);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Deleted file from data directory: {FilePath}", filePath);
                }
            }
            else if (!string.IsNullOrWhiteSpace(file.SourceUrl))
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Deleted URL datasource: {SourceType} {Url}", file.SourceType, file.SourceUrl);
                }
            }
            else
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("File not found in data directory for deletion: {FilePath}", filePath);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error deleting file metadata or file from data directory");
            }
            throw;
        }
    }

    /// <summary>
    /// Checks if a URL already exists in the datasources
    /// </summary>
    public async Task<FileMetadata?> FindDuplicateByUrlAsync(string sourceUrl, string? tenantId = null)
    {
        try
        {
            await EnsureInitializedAsync();
            var query = _context.Datasources
                .Where(f => f.SourceUrl == sourceUrl && f.SourceType == "url");

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(f => f.TenantId == tenantId);
            }

            return await query.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error checking for duplicate URL: {Url}", sourceUrl);
            }
            return null;
        }
    }

    /// <summary>
    /// Adds a URL datasource entry with hash generation for consistent duplicate detection
    /// </summary>
    public async Task<FileMetadata> AddUrlAsync(
        string sourceName,
        string sourceUrl,
        string sourceType = UrlSourceTypeClassifier.GenericUrl,
        string? mimeType = null,
        string status = "uploaded",
        string tenantId = "default")
    {
        try
        {
            // Ensure database is initialized before adding
            await EnsureInitializedAsync();

            // Generate hash for the URL for consistent duplicate detection
            var urlHash = CalculateUrlHash(sourceUrl);

            var fileMetadata = new FileMetadata
            {
                FileName = sourceName,
                OriginalFileName = sourceName,
                FilePath = string.Empty, // No physical file path for URLs
                FileSize = 0, // No file size for URLs initially
                UploadedAt = DateTime.UtcNow,
                Status = status,
                FileHash = urlHash, // Store URL hash for duplicate detection
                SourceType = sourceType,
                SourceUrl = sourceUrl,
                MimeType = mimeType ?? UrlSourceTypeClassifier.GetDefaultMimeType(sourceType),
                TenantId = tenantId
            };

            _context.Datasources.Add(fileMetadata);
            await _context.SaveChangesAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Added URL metadata to database: {SourceName}, URL: {Url}, Hash: {Hash}, Status: {Status}, Tenant: {TenantId}",
                    sourceName, sourceUrl, urlHash, status, tenantId);
            }

            return fileMetadata;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error adding URL metadata to database");
            }
            throw;
        }
    }

    public async Task<AutomaticProcessingDispatchResult> TryStartAutomaticProcessingAsync(int fileId, CancellationToken cancellationToken = default)
    {
        if (_documentProcessingCoordinator is null)
        {
            return AutomaticProcessingDispatchResult.NotAttempted();
        }

        return await _documentProcessingCoordinator.TryStartProcessingAsync(fileId, cancellationToken);
    }

    private static bool RequiresExternalCleanup(FileMetadata file)
    {
        return string.Equals(file.Status, "processing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "processed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "error", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(file.DoclingDocumentPath)
            || !string.IsNullOrWhiteSpace(file.Neo4jDocumentNodeId);
    }

    private IQueryable<FileMetadata> BuildTenantScopedQuery(int fileId, string? tenantId)
    {
        var query = _context.Datasources.Where(file => file.Id == fileId);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(file => file.TenantId == tenantId);
        }

        return query;
    }

    private void ApplyFileStatus(FileMetadata file, string status)
    {
        var normalizedStatus = NormalizeStatus(status);
        file.Status = normalizedStatus;

        switch (normalizedStatus)
        {
            case "processing":
                file.ProcessingStartedAt = DateTime.UtcNow;
                ClearProcessingArtifacts(file, clearStartedAt: false);
                break;

            case "processed":
                file.ProcessingCompletedAt = DateTime.UtcNow;
                file.ProcessingError = null;
                break;

            case "error":
                file.ProcessingCompletedAt = DateTime.UtcNow;
                break;

            case "uploaded":
                ClearProcessingArtifacts(file, clearStartedAt: true);
                break;
        }
    }

    private void ClearProcessingArtifacts(FileMetadata file, bool clearStartedAt)
    {
        if (clearStartedAt)
        {
            file.ProcessingStartedAt = null;
        }

        file.ProcessingCompletedAt = null;
        file.ProcessingError = null;
        file.DoclingDocumentPath = null;
        file.TotalPages = null;
        file.IndexingStatus = "not_requested";
        file.Neo4jDocumentNodeId = null;

        var existingPages = _context.DatasourcePages.Where(page => page.FileId == file.Id);
        _context.DatasourcePages.RemoveRange(existingPages);
    }

    private static string NormalizeStatus(string status) =>
        status.Trim().ToLowerInvariant();

    private async Task EnsureFileSchemaAsync()
    {
        if (!string.Equals(_context.Database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal))
        {
            return;
        }

        await _context.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_name = 'files' AND column_name = 'indexing_status'
                ) THEN
                    ALTER TABLE files
                    ADD COLUMN indexing_status character varying(20) NOT NULL DEFAULT 'not_requested';
                END IF;
            END $$;
            """);
    }

}
