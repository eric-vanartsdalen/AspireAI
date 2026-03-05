using AspireApp.Web.Shared;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AspireApp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileUploadController(
    FileStorageService fileStorageService,
    ILogger<FileUploadController> logger,
    IConfiguration configuration) : ControllerBase
{
    private readonly FileStorageService _fileStorageService = fileStorageService;
    private readonly ILogger<FileUploadController> _logger = logger;
    private readonly IConfiguration _configuration = configuration;

    private readonly string[] _allowedExtensions = [".pdf", ".docx", ".txt", ".md"];
    private readonly long _maxFileSize = configuration.GetValue<long?>("FileUpload:MaxFileSize") ?? 10485760;

    // Fix for CA1873: Only evaluate expensive arguments if logging is enabled.
    // Wrap expensive string interpolation or formatting in an if (_logger.IsEnabled(LogLevel.Information)) block.

    [HttpPost]
    [RequestSizeLimit(104857600)] // 100MB request size limit
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { success = false, error = "No file uploaded." });
            }

            // Validate file size
            if (file.Length > _maxFileSize)
            {
                return BadRequest(new
                {
                    success = false,
                    error = $"File size ({file.Length:N0} bytes) exceeds maximum allowed size ({_maxFileSize:N0} bytes)."
                });
            }

            // Validate file extension
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(fileExtension))
            {
                return BadRequest(new
                {
                    success = false,
                    error = $"File type '{fileExtension}' is not allowed. Allowed types: {string.Join(", ", _allowedExtensions)}"
                });
            }

            // Ensure initialization
            var initialized = await _fileStorageService.EnsureInitializedAsync();
            if (!initialized)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "File storage service initialization failed."
                });
            }

            // Calculate file hash
            string fileHash;
            using (var stream = file.OpenReadStream())
            {
                fileHash = FileStorageService.CalculateFileHash(stream);
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Calculated file hash: {Hash} for file: {FileName}", fileHash, file.FileName);
            }

            // Check for duplicate files based on hash
            var existingFile = await _fileStorageService.FindDuplicateByHashAsync(fileHash);
            if (existingFile != null)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var duplicateMessage = $"File already exists as '{existingFile.FileName}' (uploaded on {existingFile.UploadedAt:yyyy-MM-dd HH:mm:ss}). Duplicate not saved.";
                    _logger.LogInformation("Duplicate file detected. Original: {OriginalFile} (ID: {Id}), New: {NewFile}",
                        existingFile.FileName, existingFile.Id, file.FileName);
                }

                return Ok(new
                {
                    success = true,
                    isDuplicate = true,
                    fileName = file.FileName,
                    originalFileName = file.FileName,
                    length = file.Length,
                    existingFileId = existingFile.Id,
                    existingFileName = existingFile.FileName,
                    existingUploadedAt = existingFile.UploadedAt,
                    message = $"File already exists as '{existingFile.FileName}' (uploaded on {existingFile.UploadedAt:yyyy-MM-dd HH:mm:ss}). Duplicate not saved.",
                    fileHash
                });
            }

            // Generate unique filename to avoid conflicts
            var uniqueFileName = GenerateUniqueFileName(file.FileName);
            var dataDirectory = GetDataDirectory(_configuration);
            var filePath = Path.Combine(dataDirectory, uniqueFileName);

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Add file metadata to database with hash and status
            var fileMetadata = await _fileStorageService.AddFileAsync(
                uniqueFileName,
                file.FileName,
                dataDirectory,
                file.Length,
                fileHash,
                "uploaded");

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("File uploaded successfully: {FileName} -> {FilePath}, Size: {Size} bytes, Hash: {Hash}",
                    file.FileName, filePath, file.Length, fileHash);
            }

            return Ok(new
            {
                success = true,
                isDuplicate = false,
                fileName = uniqueFileName,
                originalFileName = file.FileName,
                length = file.Length,
                id = fileMetadata.Id,
                uploadedAt = fileMetadata.UploadedAt,
                status = fileMetadata.Status,
                fileHash,
                message = "File uploaded successfully."
            });
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error uploading file: {FileName}", file?.FileName);
            }
            return StatusCode(500, new
            {
                success = false,
                error = $"An error occurred while uploading the file: {ex.Message}"
            });
        }
    }

    [HttpPost("url")]
    public async Task<IActionResult> UploadUrl([FromBody] UrlUploadRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Url))
            {
                return BadRequest(new { success = false, error = "No URL provided." });
            }

            // Basic URL validation
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest(new { success = false, error = "Invalid URL format. URL must start with http:// or https://." });
            }

            // Ensure initialization
            var initialized = await _fileStorageService.EnsureInitializedAsync();
            if (!initialized)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "File storage service initialization failed."
                });
            }

            // Generate URL hash for duplicate detection
            var urlHash = FileStorageService.CalculateUrlHash(request.Url);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Calculated URL hash: {Hash} for URL: {Url}", urlHash, request.Url);
            }

            // Check for duplicate URLs based on hash (more reliable than URL comparison)
            var existingUrl = await _fileStorageService.FindDuplicateByHashAsync(urlHash);
            if (existingUrl != null)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var duplicateUrlMessage = $"URL already exists as '{existingUrl.FileName}' (added on {existingUrl.UploadedAt:yyyy-MM-dd HH:mm:ss}). Duplicate not saved.";
                    _logger.LogInformation("Duplicate URL detected. Original: {OriginalUrl} (ID: {Id}), New: {NewUrl}",
                        existingUrl.SourceUrl, existingUrl.Id, request.Url);
                }

                return Ok(new
                {
                    success = true,
                    isDuplicate = true,
                    url = request.Url,
                    existingFileId = existingUrl.Id,
                    existingFileName = existingUrl.FileName,
                    existingUploadedAt = existingUrl.UploadedAt,
                    message = $"URL already exists as '{existingUrl.FileName}' (added on {existingUrl.UploadedAt:yyyy-MM-dd HH:mm:ss}). Duplicate not saved."
                });
            }

            // Create a friendly name from URL
            var fileName = GenerateFileNameFromUrl(uri);

            // Add URL metadata to database with hash
            var fileMetadata = await _fileStorageService.AddUrlAsync(
                fileName,
                request.Url,
                "uploaded");

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Website URL added successfully: {Url}, ID: {Id}, Hash: {Hash}",
                    request.Url, fileMetadata.Id, urlHash);
            }

            return Ok(new
            {
                success = true,
                isDuplicate = false,
                url = request.Url,
                fileName,
                id = fileMetadata.Id,
                uploadedAt = fileMetadata.UploadedAt,
                status = fileMetadata.Status,
                fileHash = urlHash,
                message = "Website URL added successfully."
            });
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error adding website URL: {Url}", request?.Url);
            }
            return StatusCode(500, new
            {
                success = false,
                error = $"An error occurred while adding the website URL: {ex.Message}"
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUploadedFiles()
    {
        try
        {
            var files = await _fileStorageService.GetAllFilesAsync();
            return Ok(new { success = true, files });
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error retrieving uploaded files");
            }
            return StatusCode(500, new
            {
                success = false,
                error = $"An error occurred while retrieving files: {ex.Message}"
            });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFile(int id)
    {
        try
        {
            var success = await _fileStorageService.DeleteFileAsync(id);
            if (success)
            {
                return Ok(new { success = true, message = "File deleted successfully." });
            }
            else
            {
                return NotFound(new { success = false, error = "File not found." });
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error deleting file with ID: {Id}", id);
            }
            return StatusCode(500, new
            {
                success = false,
                error = $"An error occurred while deleting the file: {ex.Message}"
            });
        }
    }

    private static string GenerateUniqueFileName(string originalFileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        // Create filename: originalname_20240101_123456_abcd1234.ext
        return $"{nameWithoutExtension}_{timestamp}_{uniqueId}{extension}";
    }

    private static string GenerateFileNameFromUrl(Uri uri)
    {
        // Create a friendly name from the URL domain and path
        var host = uri.Host.Replace("www.", "");
        var pathPart = uri.AbsolutePath.Trim('/').Replace("/", "_");

        if (string.IsNullOrEmpty(pathPart))
        {
            pathPart = "index";
        }
        else if (pathPart.Length > 50)
        {
            pathPart = pathPart[..50];
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return $"{host}_{pathPart}_{timestamp}";
    }

    private static string GetDataDirectory(IConfiguration configuration)
    {
        var fileUploadDataDir = configuration.GetValue<string>("FileUpload:DataDirectory");
        if (!string.IsNullOrEmpty(fileUploadDataDir))
        {
            return Path.IsPathRooted(fileUploadDataDir)
                ? fileUploadDataDir
                : Path.Combine(Directory.GetCurrentDirectory(), fileUploadDataDir);
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}

public class UrlUploadRequest
{
    public string Url { get; set; } = string.Empty;
}