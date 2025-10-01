using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AspireApp.Web.Data;

namespace AspireApp.Web.Shared
{
    /// <summary>
    /// Service to manage synchronization between FileMetadata (Files table) and Document (documents table)
    /// This ensures compatibility between C# and Python services
    /// </summary>
    public class DocumentBridgeService
    {
        private readonly UploadDbContext _context;
        private readonly ILogger<DocumentBridgeService> _logger;

        public DocumentBridgeService(UploadDbContext context, ILogger<DocumentBridgeService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Ensure both Files and documents tables exist and are properly configured
        /// </summary>
        public async Task<bool> EnsureDatabaseSchemaAsync()
        {
            try
            {
                // This will create tables if they don't exist
                await _context.Database.EnsureCreatedAsync();
                
                // Check if both tables exist
                var tablesExist = await CheckRequiredTablesExistAsync();
                
                if (tablesExist)
                {
                    _logger.LogInformation("Database schema is properly configured");
                    return true;
                }
                else
                {
                    _logger.LogWarning("Some required tables are missing from database schema");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring database schema");
                return false;
            }
        }

        /// <summary>
        /// Check if all required tables exist
        /// </summary>
        private async Task<bool> CheckRequiredTablesExistAsync()
        {
            try
            {
                // Test if Files table exists
                var filesExist = await _context.Database.ExecuteSqlRawAsync(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Files'") >= 0;

                // Test if documents table exists  
                var documentsExist = await _context.Database.ExecuteSqlRawAsync(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='documents'") >= 0;

                return true; // If we got here without exception, tables exist
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking table existence");
                return false;
            }
        }

        /// <summary>
        /// Sync FileMetadata records to Documents table
        /// </summary>
        public async Task<int> SyncFileMetadataToDocumentsAsync()
        {
            try
            {
                var syncedCount = 0;

                // Get FileMetadata records that don't have corresponding Documents
                var unsyncedFiles = await _context.Files
                    .Where(f => !_context.Documents.Any(d => d.FileName == f.FileName))
                    .ToListAsync();

                foreach (var file in unsyncedFiles)
                {
                    var document = CreateDocumentFromFileMetadata(file);
                    _context.Documents.Add(document);
                    syncedCount++;
                }

                if (syncedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Synced {syncedCount} FileMetadata records to Documents table");
                }

                return syncedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing FileMetadata to Documents");
                return 0;
            }
        }

        /// <summary>
        /// Sync Documents records to FileMetadata table
        /// </summary>
        public async Task<int> SyncDocumentsToFileMetadataAsync()
        {
            try
            {
                var syncedCount = 0;

                // Get Documents that don't have corresponding FileMetadata
                var unsyncedDocuments = await _context.Documents
                    .Where(d => !_context.Files.Any(f => f.FileName == d.FileName))
                    .ToListAsync();

                foreach (var document in unsyncedDocuments)
                {
                    var fileMetadata = CreateFileMetadataFromDocument(document);
                    _context.Files.Add(fileMetadata);
                    syncedCount++;
                }

                if (syncedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Synced {syncedCount} Documents records to FileMetadata table");
                }

                return syncedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Documents to FileMetadata");
                return 0;
            }
        }

        /// <summary>
        /// Create Document from FileMetadata
        /// </summary>
        private Document CreateDocumentFromFileMetadata(FileMetadata fileMetadata)
        {
            return new Document
            {
                FileName = fileMetadata.FileName,
                OriginalFileName = ExtractOriginalFileName(fileMetadata.FileName),
                FilePath = fileMetadata.FileName, // Use filename as path for now
                FileSize = fileMetadata.Size,
                MimeType = GetMimeTypeFromFileName(fileMetadata.FileName),
                UploadDate = fileMetadata.UploadedAt,
                Processed = fileMetadata.Status.ToLower() == "processed",
                ProcessingStatus = ConvertFileStatusToDocumentStatus(fileMetadata.Status)
            };
        }

        /// <summary>
        /// Create FileMetadata from Document
        /// </summary>
        private FileMetadata CreateFileMetadataFromDocument(Document document)
        {
            return new FileMetadata
            {
                FileName = document.FileName,
                Size = document.FileSize ?? 0,
                UploadedAt = document.UploadDate,
                Status = ConvertDocumentStatusToFileStatus(document.ProcessingStatus),
                FileHash = string.Empty // Default empty hash
            };
        }

        /// <summary>
        /// Extract original filename from unique filename
        /// </summary>
        private string ExtractOriginalFileName(string uniqueFileName)
        {
            // Format: originalname_20240101_123456_abcd1234.ext
            var parts = uniqueFileName.Split('_');
            if (parts.Length >= 3)
            {
                // Check if second part looks like a date (8 digits)
                if (parts[1].Length == 8 && parts[1].All(char.IsDigit))
                {
                    return parts[0] + Path.GetExtension(uniqueFileName);
                }
            }
            return uniqueFileName;
        }

        /// <summary>
        /// Get MIME type from filename
        /// </summary>
        private string GetMimeTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Convert FileMetadata status to Document processing status
        /// </summary>
        private string ConvertFileStatusToDocumentStatus(string fileStatus)
        {
            return fileStatus.ToLowerInvariant() switch
            {
                "uploaded" => "pending",
                "pending" => "pending",
                "processed" => "completed",
                "processing" => "processing",
                "error" => "error",
                "failed" => "error",
                _ => "pending"
            };
        }

        /// <summary>
        /// Convert Document processing status to FileMetadata status
        /// </summary>
        private string ConvertDocumentStatusToFileStatus(string documentStatus)
        {
            return documentStatus.ToLowerInvariant() switch
            {
                "pending" => "Uploaded",
                "processing" => "Processing", 
                "completed" => "Processed",
                "error" => "Error",
                "failed" => "Error",
                _ => "Uploaded"
            };
        }

        /// <summary>
        /// Get synchronization status
        /// </summary>
        public async Task<SyncStatus> GetSyncStatusAsync()
        {
            try
            {
                var filesCount = await _context.Files.CountAsync();
                var documentsCount = await _context.Documents.CountAsync();

                var unsyncedFiles = await _context.Files
                    .Where(f => !_context.Documents.Any(d => d.FileName == f.FileName))
                    .CountAsync();

                var unsyncedDocuments = await _context.Documents
                    .Where(d => !_context.Files.Any(f => f.FileName == d.FileName))
                    .CountAsync();

                return new SyncStatus
                {
                    FilesCount = filesCount,
                    DocumentsCount = documentsCount,
                    UnsyncedFiles = unsyncedFiles,
                    UnsyncedDocuments = unsyncedDocuments,
                    IsHealthy = unsyncedFiles == 0 && unsyncedDocuments == 0,
                    LastChecked = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync status");
                return new SyncStatus
                {
                    IsHealthy = false,
                    ErrorMessage = ex.Message,
                    LastChecked = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Perform a full synchronization between tables
        /// </summary>
        public async Task<SyncResult> PerformFullSyncAsync()
        {
            try
            {
                var filesSynced = await SyncFileMetadataToDocumentsAsync();
                var documentsSynced = await SyncDocumentsToFileMetadataAsync();

                var finalStatus = await GetSyncStatusAsync();

                return new SyncResult
                {
                    Success = true,
                    FilesSyncedToDocuments = filesSynced,
                    DocumentsSyncedToFiles = documentsSynced,
                    FinalStatus = finalStatus,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing full sync");
                return new SyncResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Perform health check on the database schema and sync status
        /// </summary>
        public async Task<HealthCheckResult> PerformHealthCheckAsync()
        {
            try
            {
                var canConnect = await _context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    return new HealthCheckResult
                    {
                        OverallHealthy = false,
                        ErrorMessage = "Cannot connect to database",
                        Timestamp = DateTime.UtcNow
                    };
                }

                var schemaOk = await EnsureDatabaseSchemaAsync();
                var syncStatus = await GetSyncStatusAsync();

                return new HealthCheckResult
                {
                    OverallHealthy = schemaOk && syncStatus.IsHealthy,
                    CanConnect = canConnect,
                    SchemaHealthy = schemaOk,
                    SyncHealthy = syncStatus.IsHealthy,
                    SyncStatus = syncStatus,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing health check");
                return new HealthCheckResult
                {
                    OverallHealthy = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }
    }

    /// <summary>
    /// Synchronization status information
    /// </summary>
    public class SyncStatus
    {
        public int FilesCount { get; set; }
        public int DocumentsCount { get; set; }
        public int UnsyncedFiles { get; set; }
        public int UnsyncedDocuments { get; set; }
        public bool IsHealthy { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime LastChecked { get; set; }
    }

    /// <summary>
    /// Synchronization operation result
    /// </summary>
    public class SyncResult
    {
        public bool Success { get; set; }
        public int FilesSyncedToDocuments { get; set; }
        public int DocumentsSyncedToFiles { get; set; }
        public SyncStatus? FinalStatus { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Health check result
    /// </summary>
    public class HealthCheckResult
    {
        public bool OverallHealthy { get; set; }
        public bool CanConnect { get; set; }
        public bool SchemaHealthy { get; set; }
        public bool SyncHealthy { get; set; }
        public SyncStatus? SyncStatus { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }
}