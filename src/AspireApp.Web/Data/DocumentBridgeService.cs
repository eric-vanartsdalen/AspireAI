using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AspireApp.Web.Data
{
    /// <summary>
    /// Service to bridge between the .NET file upload system and Python document processing service
    /// Handles synchronization of data between FileMetadata and Document entities
    /// </summary>
    public class DocumentBridgeService
    {
        private readonly UploadDbContext _context;
        private readonly ILogger<DocumentBridgeService> _logger;
        private readonly IConfiguration _configuration;

        public DocumentBridgeService(
            UploadDbContext context,
            ILogger<DocumentBridgeService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Ensures database schema exists for both systems
        /// </summary>
        public async Task<bool> EnsureDatabaseSchemaAsync()
        {
            try
            {
                // Check if database can connect
                var canConnect = await _context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    _logger.LogInformation("Database cannot connect, creating schema...");
                    await _context.Database.EnsureCreatedAsync();
                    _logger.LogInformation("Database schema created successfully");
                }

                // Verify tables exist by attempting to query them
                await _context.Files.CountAsync();
                await _context.Documents.CountAsync();
                await _context.ProcessedDocuments.CountAsync();
                await _context.DocumentPages.CountAsync();

                _logger.LogInformation("All required database tables verified");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring database schema");
                return false;
            }
        }

        /// <summary>
        /// Synchronizes FileMetadata entries to Document entries for Python service compatibility
        /// </summary>
        public async Task<int> SyncFileMetadataToDocumentsAsync()
        {
            try
            {
                var dataDirectory = GetDataDirectory();
                
                // Get all FileMetadata entries that don't have corresponding Document entries
                var fileMetadataWithoutDocuments = await _context.Files
                    .Where(fm => !_context.Documents.Any(d => d.FileName == fm.FileName))
                    .ToListAsync();

                if (!fileMetadataWithoutDocuments.Any())
                {
                    _logger.LogInformation("No FileMetadata entries to sync");
                    return 0;
                }

                var syncedCount = 0;
                foreach (var fileMetadata in fileMetadataWithoutDocuments)
                {
                    try
                    {
                        var document = Document.FromFileMetadata(fileMetadata, dataDirectory);
                        _context.Documents.Add(document);
                        syncedCount++;
                        
                        _logger.LogDebug("Created Document entity for file: {FileName}", fileMetadata.FileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create Document entity for file: {FileName}", fileMetadata.FileName);
                    }
                }

                if (syncedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Synced {Count} FileMetadata entries to Documents table", syncedCount);
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
        /// Creates a Document entity when a new file is uploaded
        /// </summary>
        public async Task<Document?> CreateDocumentFromFileMetadataAsync(FileMetadata fileMetadata)
        {
            try
            {
                var dataDirectory = GetDataDirectory();
                var document = Document.FromFileMetadata(fileMetadata, dataDirectory);
                
                _context.Documents.Add(document);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Created Document entity (ID: {DocumentId}) for file: {FileName}", 
                    document.Id, fileMetadata.FileName);
                
                return document;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Document entity for file: {FileName}", fileMetadata.FileName);
                return null;
            }
        }

        /// <summary>
        /// Updates processing status for both Document and FileMetadata entities
        /// </summary>
        public async Task<bool> UpdateProcessingStatusAsync(int documentId, string status)
        {
            try
            {
                var document = await _context.Documents.FindAsync(documentId);
                if (document == null)
                {
                    return false;
                }

                // Update Document entity
                document.ProcessingStatus = status;
                document.Processed = status == "completed";

                // Find and update corresponding FileMetadata if it exists
                var fileMetadata = await _context.Files
                    .FirstOrDefaultAsync(f => f.FileName == document.FileName);
                
                if (fileMetadata != null)
                {
                    fileMetadata.Status = ConvertDocumentStatusToFileStatus(status);
                }

                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Updated processing status for Document ID {DocumentId}: {Status}", 
                    documentId, status);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating processing status for Document ID: {DocumentId}", documentId);
                return false;
            }
        }

        /// <summary>
        /// Gets all unprocessed documents for the Python service
        /// </summary>
        public async Task<List<Document>> GetUnprocessedDocumentsAsync()
        {
            try
            {
                return await _context.Documents
                    .Where(d => !d.Processed)
                    .OrderBy(d => d.UploadDate)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving unprocessed documents");
                return new List<Document>();
            }
        }

        /// <summary>
        /// Gets processing statistics
        /// </summary>
        public async Task<DocumentProcessingStats> GetProcessingStatsAsync()
        {
            try
            {
                var totalFiles = await _context.Files.CountAsync();
                var totalDocuments = await _context.Documents.CountAsync();
                var processedDocuments = await _context.Documents.CountAsync(d => d.Processed);
                var pendingDocuments = await _context.Documents.CountAsync(d => !d.Processed);
                var totalProcessedDocs = await _context.ProcessedDocuments.CountAsync();
                var totalPages = await _context.DocumentPages.CountAsync();

                return new DocumentProcessingStats
                {
                    TotalFiles = totalFiles,
                    TotalDocuments = totalDocuments,
                    ProcessedDocuments = processedDocuments,
                    PendingDocuments = pendingDocuments,
                    TotalProcessedDocuments = totalProcessedDocs,
                    TotalPages = totalPages,
                    SyncedPercentage = totalFiles > 0 ? (totalDocuments * 100.0 / totalFiles) : 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving processing statistics");
                return new DocumentProcessingStats();
            }
        }

        /// <summary>
        /// Performs health check on the document bridge system
        /// </summary>
        public async Task<DocumentBridgeHealthCheck> PerformHealthCheckAsync()
        {
            var healthCheck = new DocumentBridgeHealthCheck();

            try
            {
                // Check database connectivity
                healthCheck.DatabaseConnected = await _context.Database.CanConnectAsync();

                if (healthCheck.DatabaseConnected)
                {
                    // Check if all tables exist and are accessible
                    try
                    {
                        await _context.Files.CountAsync();
                        healthCheck.FilesTableAccessible = true;
                    }
                    catch { healthCheck.FilesTableAccessible = false; }

                    try
                    {
                        await _context.Documents.CountAsync();
                        healthCheck.DocumentsTableAccessible = true;
                    }
                    catch { healthCheck.DocumentsTableAccessible = false; }

                    try
                    {
                        await _context.ProcessedDocuments.CountAsync();
                        healthCheck.ProcessedDocumentsTableAccessible = true;
                    }
                    catch { healthCheck.ProcessedDocumentsTableAccessible = false; }

                    try
                    {
                        await _context.DocumentPages.CountAsync();
                        healthCheck.DocumentPagesTableAccessible = true;
                    }
                    catch { healthCheck.DocumentPagesTableAccessible = false; }

                    // Check sync status
                    if (healthCheck.FilesTableAccessible && healthCheck.DocumentsTableAccessible)
                    {
                        var fileCount = await _context.Files.CountAsync();
                        var docCount = await _context.Documents.CountAsync();
                        healthCheck.SyncStatus = fileCount == docCount ? "Synced" : "Out of Sync";
                    }
                }

                healthCheck.OverallHealthy = healthCheck.DatabaseConnected &&
                                          healthCheck.FilesTableAccessible &&
                                          healthCheck.DocumentsTableAccessible &&
                                          healthCheck.ProcessedDocumentsTableAccessible &&
                                          healthCheck.DocumentPagesTableAccessible;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing health check");
                healthCheck.ErrorMessage = ex.Message;
            }

            return healthCheck;
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

        private static string ConvertDocumentStatusToFileStatus(string documentStatus)
        {
            return documentStatus.ToLowerInvariant() switch
            {
                "pending" => "Uploaded",
                "completed" => "Processed",
                "error" => "Error",
                "processing" => "Processing",
                _ => "Uploaded"
            };
        }
    }

    /// <summary>
    /// Statistics about document processing
    /// </summary>
    public class DocumentProcessingStats
    {
        public int TotalFiles { get; set; }
        public int TotalDocuments { get; set; }
        public int ProcessedDocuments { get; set; }
        public int PendingDocuments { get; set; }
        public int TotalProcessedDocuments { get; set; }
        public int TotalPages { get; set; }
        public double SyncedPercentage { get; set; }
    }

    /// <summary>
    /// Health check result for the document bridge system
    /// </summary>
    public class DocumentBridgeHealthCheck
    {
        public bool DatabaseConnected { get; set; }
        public bool FilesTableAccessible { get; set; }
        public bool DocumentsTableAccessible { get; set; }
        public bool ProcessedDocumentsTableAccessible { get; set; }
        public bool DocumentPagesTableAccessible { get; set; }
        public string SyncStatus { get; set; } = "Unknown";
        public bool OverallHealthy { get; set; }
        public string? ErrorMessage { get; set; }
    }
}