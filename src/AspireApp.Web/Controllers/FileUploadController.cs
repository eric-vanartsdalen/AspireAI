using AspireApp.Web.Data;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AspireApp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileUploadController : ControllerBase
{
    private readonly FileStorageService _fileStorageService;
    private readonly ILogger<FileUploadController> _logger;
    private readonly IConfiguration _configuration;

    private readonly string[] _allowedExtensions = { ".pdf", ".docx", ".txt", ".md" };
    private readonly long _maxFileSize;

    public FileUploadController(
        FileStorageService fileStorageService,
        ILogger<FileUploadController> logger,
        IConfiguration configuration)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
        _configuration = configuration;
        _maxFileSize = configuration.GetValue<long?>("FileUpload:MaxFileSize") ?? 10485760; // 10MB default
    }

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
                return BadRequest(new { 
                    success = false, 
                    error = $"File size ({file.Length:N0} bytes) exceeds maximum allowed size ({_maxFileSize:N0} bytes)." 
                });
            }

            // Validate file extension
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(fileExtension))
            {
                return BadRequest(new { 
                    success = false, 
                    error = $"File type '{fileExtension}' is not allowed. Allowed types: {string.Join(", ", _allowedExtensions)}" 
                });
            }

            // Ensure initialization
            var initialized = await _fileStorageService.EnsureInitializedAsync();
            if (!initialized)
            {
                return StatusCode(500, new { 
                    success = false, 
                    error = "File storage service initialization failed." 
                });
            }

            // Generate unique filename to avoid conflicts
            var uniqueFileName = GenerateUniqueFileName(file.FileName);
            var dataDirectory = GetDataDirectory();
            var filePath = Path.Combine(dataDirectory, uniqueFileName);

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Add file metadata to database
            var fileMetadata = await _fileStorageService.AddFileAsync(uniqueFileName, file.Length);

            _logger.LogInformation("File uploaded successfully: {FileName} -> {FilePath}, Size: {Size} bytes", 
                file.FileName, filePath, file.Length);

            return Ok(new { 
                success = true, 
                fileName = uniqueFileName,
                originalFileName = file.FileName,
                length = file.Length,
                id = fileMetadata.Id,
                uploadedAt = fileMetadata.UploadedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {FileName}", file?.FileName);
            return StatusCode(500, new { 
                success = false, 
                error = $"An error occurred while uploading the file: {ex.Message}" 
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUploadedFiles()
    {
        try
        {
            var files = await _fileStorageService.GetAllFilesAsync();
            return Ok(new { success = true, files = files });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving uploaded files");
            return StatusCode(500, new { 
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
            _logger.LogError(ex, "Error deleting file with ID: {Id}", id);
            return StatusCode(500, new { 
                success = false, 
                error = $"An error occurred while deleting the file: {ex.Message}" 
            });
        }
    }

    private string GenerateUniqueFileName(string originalFileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        
        // Create filename: originalname_20240101_123456_abcd1234.ext
        return $"{nameWithoutExtension}_{timestamp}_{uniqueId}{extension}";
    }

    private string GetDataDirectory()
    {
        var fileUploadDataDir = _configuration.GetValue<string>("FileUpload:DataDirectory");
        if (!string.IsNullOrEmpty(fileUploadDataDir))
        {
            return Path.IsPathRooted(fileUploadDataDir)
                ? fileUploadDataDir
                : Path.Combine(Directory.GetCurrentDirectory(), fileUploadDataDir);
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}