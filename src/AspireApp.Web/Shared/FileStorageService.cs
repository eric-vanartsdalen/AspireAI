using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using AspireApp.Web.Data;
using AspireApp.Web.Shared;

namespace AspireApp.Web.Data;

public class FileStorageService
{
    private readonly UploadDbContext _context;
    private readonly ILogger<FileStorageService> _logger;
    private readonly string _dataDirectory;
    private readonly DocumentBridgeService _bridgeService;

    public FileStorageService(
        UploadDbContext context, 
        ILogger<FileStorageService> logger, 
        string dataDirectory,
        DocumentBridgeService bridgeService)
    {
        _context = context;
        _logger = logger;
        _dataDirectory = dataDirectory;
        _bridgeService = bridgeService;
    }

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
                _logger.LogInformation("Created data directory: {DataDirectory}", _dataDirectory);
            }

            // Ensure database schema exists for both systems
            var schemaInitialized = await _bridgeService.EnsureDatabaseSchemaAsync();
            if (!schemaInitialized)
            {
                _logger.LogError("Failed to initialize database schema");
                return false;
            }

            // Sync any existing FileMetadata to Documents
            var syncedCount = await _bridgeService.SyncFileMetadataToDocumentsAsync();
            if (syncedCount > 0)
            {
                _logger.LogInformation("Synced {Count} existing files to Documents table", syncedCount);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring database and directory initialization");
            return false;
        }
    }

    /// <summary>
    /// Calculates SHA256 hash of file content
    /// </summary>
    public static string CalculateFileHash(Stream fileStream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(fileStream);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Calculates SHA256 hash of file content from file path
    /// </summary>
    public static async Task<string> CalculateFileHashAsync(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = await Task.Run(() => sha256.ComputeHash(fileStream));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Checks if a file with the same hash already exists
    /// </summary>
    public async Task<FileMetadata?> FindDuplicateByHashAsync(string fileHash)
    {
        try
        {
            await EnsureInitializedAsync();
            return await _context.Files
                .FirstOrDefaultAsync(f => f.FileHash == fileHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for duplicate files with hash: {FileHash}", fileHash);
            return null;
        }
    }

    /// <summary>
    /// Adds a file with hash calculation and duplicate detection
    /// Also creates corresponding Document entity for Python service compatibility
    /// </summary>
    public async Task<FileMetadata> AddFileAsync(string fileName, long size, string fileHash, string status = "Uploaded")
    {
        try
        {
            // Ensure database is initialized before adding files
            await EnsureInitializedAsync();

            var fileMetadata = new FileMetadata
            {
                FileName = fileName,
                Size = size,
                UploadedAt = DateTime.UtcNow,
                Status = status,
                FileHash = fileHash
            };

            _context.Files.Add(fileMetadata);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Added file metadata to database: {FileName}, Size: {Size}, Hash: {Hash}, Status: {Status}", 
                fileName, size, fileHash, status);

            // Create corresponding Document entity for Python service
            var document = await _bridgeService.CreateDocumentFromFileMetadataAsync(fileMetadata);
            if (document != null)
            {
                _logger.LogInformation("Created corresponding Document entity (ID: {DocumentId}) for file: {FileName}", 
                    document.Id, fileName);
            }
            else
            {
                _logger.LogWarning("Failed to create Document entity for file: {FileName}", fileName);
            }

            return fileMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding file metadata to database");
            throw;
        }
    }

    /// <summary>
    /// Legacy method for backward compatibility - adds file without hash
    /// </summary>
    public async Task<FileMetadata> AddFileAsync(string fileName, long size)
    {
        return await AddFileAsync(fileName, size, string.Empty, "Pending");
    }

    /// <summary>
    /// Updates file status and corresponding document status
    /// </summary>
    public async Task<bool> UpdateFileStatusAsync(int fileId, string status)
    {
        try
        {
            await EnsureInitializedAsync();
            
            var file = await _context.Files.FindAsync(fileId);
            if (file == null)
            {
                return false;
            }

            file.Status = status;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Updated file status: {FileId}, Status: {Status}", fileId, status);

            // Update corresponding Document entity
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.FileName == file.FileName);
            
            if (document != null)
            {
                var documentStatus = ConvertFileStatusToDocumentStatus(status);
                await _bridgeService.UpdateProcessingStatusAsync(document.Id, documentStatus);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file status for file ID: {FileId}", fileId);
            throw;
        }
    }

    /// <summary>
    /// Updates file hash for existing file
    /// </summary>
    public async Task<bool> UpdateFileHashAsync(int fileId, string fileHash)
    {
        try
        {
            await EnsureInitializedAsync();
            
            var file = await _context.Files.FindAsync(fileId);
            if (file == null)
            {
                return false;
            }

            file.FileHash = fileHash;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Updated file hash: {FileId}, Hash: {Hash}", fileId, fileHash);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file hash for file ID: {FileId}", fileId);
            throw;
        }
    }

    public async Task<List<FileMetadata>> GetAllFilesAsync()
    {
        try
        {
            // Ensure database is initialized before querying
            var initialized = await EnsureInitializedAsync();
            if (!initialized)
            {
                _logger.LogWarning("Database initialization failed, returning empty list");
                return new List<FileMetadata>();
            }

            return await _context.Files.OrderByDescending(f => f.UploadedAt).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving files from database");
            // Return empty list instead of throwing to allow UI to function
            return new List<FileMetadata>();
        }
    }

    public async Task<bool> DeleteFileAsync(int id)
    {
        try
        {
            // Ensure database is initialized before deleting
            await EnsureInitializedAsync();

            var file = await _context.Files.FindAsync(id);
            if (file == null)
            {
                return false;
            }
            var fileName = file.FileName;
            var filePath = Path.Combine(_dataDirectory, fileName);

            // Delete corresponding Document entity first (cascade will handle related entities)
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.FileName == fileName);
            
            if (document != null)
            {
                _context.Documents.Remove(document);
                _logger.LogInformation("Deleted corresponding Document entity (ID: {DocumentId}) for file: {FileName}", 
                    document.Id, fileName);
            }

            _context.Files.Remove(file);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Deleted file metadata from database: {FileName}", fileName);

            // Delete the physical file if it exists
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted file from data directory: {FilePath}", filePath);
            }
            else
            {
                _logger.LogWarning("File not found in data directory for deletion: {FilePath}", filePath);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file metadata or file from data directory");
            throw;
        }
    }

    /// <summary>
    /// Gets processing statistics from the bridge service
    /// </summary>
    public async Task<DocumentProcessingStats> GetProcessingStatsAsync()
    {
        return await _bridgeService.GetProcessingStatsAsync();
    }

    /// <summary>
    /// Performs health check on the file storage and bridge system
    /// </summary>
    public async Task<DocumentBridgeHealthCheck> PerformHealthCheckAsync()
    {
        var health = await _bridgeService.PerformHealthCheckAsync();
        return new DocumentBridgeHealthCheck
        {
            OverallHealthy = health.OverallHealthy,
            DatabaseConnected = health.CanConnect,
            DocumentsTableAccessible = health.SchemaHealthy,
            SyncStatus = health.SyncStatus?.IsHealthy == true ? "In Sync" : "Out of Sync",
            ErrorMessage = health.ErrorMessage,
            Timestamp = health.Timestamp
        };
    }

    private static string ConvertFileStatusToDocumentStatus(string fileStatus)
    {
        return fileStatus.ToLowerInvariant() switch
        {
            "uploaded" => "pending",
            "processed" => "completed",
            "error" => "error",
            "processing" => "processing",
            _ => "pending"
        };
    }
}
