using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using AspireApp.Web.Data;

namespace AspireApp.Web.Shared;

public class FileStorageService(
    UploadDbContext context,
    ILogger<FileStorageService> logger,
    string dataDirectory)
{
    private readonly UploadDbContext _context = context;
    private readonly ILogger<FileStorageService> _logger = logger;
    private readonly string _dataDirectory = dataDirectory;

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
    public async Task<FileMetadata?> FindDuplicateByHashAsync(string fileHash)
    {
        try
        {
            await EnsureInitializedAsync();
            return await _context.Datasources
                .FirstOrDefaultAsync(f => f.FileHash == fileHash);
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
    public async Task<FileMetadata> AddFileAsync(string fileName, string originalFilename, string fileDirectory, long size, string fileHash, string status = "uploaded")
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
                SourceType = "upload"
            };

            _context.Datasources.Add(fileMetadata);
            await _context.SaveChangesAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Added file metadata to database: {FileName}, Size: {Size}, Hash: {Hash}, Status: {Status}",
                    fileName, size, fileHash, status);
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
    public async Task<bool> UpdateFileStatusAsync(int fileId, string status)
    {
        try
        {
            await EnsureInitializedAsync();

            var file = await _context.Datasources.FindAsync(fileId);
            if (file == null)
            {
                return false;
            }

            file.Status = status;

            // Update timestamp fields based on status
            if (status == "processing")
            {
                file.ProcessingStartedAt = DateTime.UtcNow;
            }
            else if (status == "processed" || status == "error")
            {
                file.ProcessingCompletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Updated file status: {FileId}, Status: {Status}", fileId, status);
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

    public async Task<List<FileMetadata>> GetAllFilesAsync()
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

            return await _context.Datasources.OrderByDescending(f => f.UploadedAt).ToListAsync();
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

    public async Task<bool> DeleteFileAsync(int id)
    {
        try
        {
            // Ensure database is initialized before deleting
            await EnsureInitializedAsync();

            var file = await _context.Datasources.FindAsync(id);
            if (file == null)
            {
                return false;
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
            else if (file.SourceType == "url")
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Deleted URL datasource: {Url}", file.SourceUrl);
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
    public async Task<FileMetadata?> FindDuplicateByUrlAsync(string sourceUrl)
    {
        try
        {
            await EnsureInitializedAsync();
            return await _context.Datasources
                .FirstOrDefaultAsync(f => f.SourceUrl == sourceUrl && f.SourceType == "url");
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
    public async Task<FileMetadata> AddUrlAsync(string sourceName, string sourceUrl, string status = "uploaded")
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
                SourceType = "url",
                SourceUrl = sourceUrl,
                MimeType = "text/html" // Default to HTML for web pages
            };

            _context.Datasources.Add(fileMetadata);
            await _context.SaveChangesAsync();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Added URL metadata to database: {SourceName}, URL: {Url}, Hash: {Hash}, Status: {Status}",
                    sourceName, sourceUrl, urlHash, status);
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
}
