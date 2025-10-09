using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using AspireApp.Web.Data;

namespace AspireApp.Web.Data;

public class FileStorageService
{
    private readonly UploadDbContext _context;
    private readonly ILogger<FileStorageService> _logger;
    private readonly string _dataDirectory;

    public FileStorageService(
        UploadDbContext context, 
        ILogger<FileStorageService> logger, 
        string dataDirectory)
    {
        _context = context;
        _logger = logger;
        _dataDirectory = dataDirectory;
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

            // Ensure database can be accessed
            var canConnect = await _context.Database.CanConnectAsync();
            if (!canConnect)
            {
                _logger.LogError("Cannot connect to database");
                return false;
            }

            // Ensure database schema is created (EF Core handles this)
            await _context.Database.EnsureCreatedAsync();

            _logger.LogInformation("Database and directory initialized successfully");
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

            _context.Files.Add(fileMetadata);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Added file metadata to database: {FileName}, Size: {Size}, Hash: {Hash}, Status: {Status}", 
                fileName, size, fileHash, status);

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
            
            var file = await _context.Files.FindAsync(fileId);
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
            
            _logger.LogInformation("Updated file status: {FileId}, Status: {Status}", fileId, status);
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

            // EF Core will cascade delete related document_pages records
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
}
