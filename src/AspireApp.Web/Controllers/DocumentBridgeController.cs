using AspireApp.Web.Data;
using Microsoft.AspNetCore.Mvc;

namespace AspireApp.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentBridgeController : ControllerBase
    {
        private readonly DocumentBridgeService _bridgeService;
        private readonly FileStorageService _fileStorageService;
        private readonly ILogger<DocumentBridgeController> _logger;

        public DocumentBridgeController(
            DocumentBridgeService bridgeService,
            FileStorageService fileStorageService,
            ILogger<DocumentBridgeController> logger)
        {
            _bridgeService = bridgeService;
            _fileStorageService = fileStorageService;
            _logger = logger;
        }

        /// <summary>
        /// Gets the health status of the document bridge system
        /// </summary>
        [HttpGet("health")]
        public async Task<IActionResult> GetHealthStatus()
        {
            try
            {
                var healthCheck = await _bridgeService.PerformHealthCheckAsync();
                
                if (healthCheck.OverallHealthy)
                {
                    return Ok(healthCheck);
                }
                else
                {
                    return StatusCode(503, healthCheck); // Service Unavailable
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing health check");
                return StatusCode(500, new { error = "Health check failed", message = ex.Message });
            }
        }

        /// <summary>
        /// Gets processing statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetProcessingStats()
        {
            try
            {
                var stats = await _bridgeService.GetProcessingStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving processing statistics");
                return StatusCode(500, new { error = "Failed to retrieve statistics", message = ex.Message });
            }
        }

        /// <summary>
        /// Synchronizes FileMetadata entries to Document entries
        /// </summary>
        [HttpPost("sync")]
        public async Task<IActionResult> SyncFileMetadataToDocuments()
        {
            try
            {
                var syncedCount = await _bridgeService.SyncFileMetadataToDocumentsAsync();
                return Ok(new { 
                    success = true, 
                    syncedCount = syncedCount,
                    message = $"Successfully synced {syncedCount} files to Documents table"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing FileMetadata to Documents");
                return StatusCode(500, new { 
                    success = false, 
                    error = "Sync operation failed", 
                    message = ex.Message 
                });
            }
        }

        /// <summary>
        /// Gets all unprocessed documents for the Python service
        /// </summary>
        [HttpGet("unprocessed")]
        public async Task<IActionResult> GetUnprocessedDocuments()
        {
            try
            {
                var documents = await _bridgeService.GetUnprocessedDocumentsAsync();
                return Ok(new { success = true, documents = documents });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving unprocessed documents");
                return StatusCode(500, new { 
                    success = false, 
                    error = "Failed to retrieve unprocessed documents", 
                    message = ex.Message 
                });
            }
        }

        /// <summary>
        /// Updates processing status for a document
        /// </summary>
        [HttpPut("{documentId}/status")]
        public async Task<IActionResult> UpdateProcessingStatus(int documentId, [FromBody] UpdateStatusRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Status))
                {
                    return BadRequest(new { success = false, error = "Status is required" });
                }

                var success = await _bridgeService.UpdateProcessingStatusAsync(documentId, request.Status);
                
                if (success)
                {
                    return Ok(new { 
                        success = true, 
                        message = $"Processing status updated to '{request.Status}' for document {documentId}"
                    });
                }
                else
                {
                    return NotFound(new { 
                        success = false, 
                        error = $"Document with ID {documentId} not found"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating processing status for document {DocumentId}", documentId);
                return StatusCode(500, new { 
                    success = false, 
                    error = "Failed to update processing status", 
                    message = ex.Message 
                });
            }
        }

        /// <summary>
        /// Initializes the database schema (creates tables if they don't exist)
        /// </summary>
        [HttpPost("initialize")]
        public async Task<IActionResult> InitializeDatabase()
        {
            try
            {
                var success = await _bridgeService.EnsureDatabaseSchemaAsync();
                
                if (success)
                {
                    // Also perform sync after initialization
                    var syncedCount = await _bridgeService.SyncFileMetadataToDocumentsAsync();
                    
                    return Ok(new { 
                        success = true, 
                        message = "Database schema initialized successfully",
                        syncedCount = syncedCount
                    });
                }
                else
                {
                    return StatusCode(500, new { 
                        success = false, 
                        error = "Failed to initialize database schema"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing database schema");
                return StatusCode(500, new { 
                    success = false, 
                    error = "Database initialization failed", 
                    message = ex.Message 
                });
            }
        }

        /// <summary>
        /// Gets comprehensive system status including both file storage and document bridge
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetSystemStatus()
        {
            try
            {
                var healthCheck = await _bridgeService.PerformHealthCheckAsync();
                var stats = await _bridgeService.GetProcessingStatsAsync();
                
                var systemStatus = new
                {
                    timestamp = DateTime.UtcNow,
                    health = healthCheck,
                    statistics = stats,
                    recommendations = GenerateRecommendations(healthCheck, stats)
                };

                return Ok(systemStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving system status");
                return StatusCode(500, new { 
                    error = "Failed to retrieve system status", 
                    message = ex.Message 
                });
            }
        }

        private static List<string> GenerateRecommendations(DocumentBridgeHealthCheck healthCheck, DocumentProcessingStats stats)
        {
            var recommendations = new List<string>();

            if (!healthCheck.OverallHealthy)
            {
                recommendations.Add("System health check failed - investigate database connectivity");
            }

            if (!healthCheck.DatabaseConnected)
            {
                recommendations.Add("Database connection failed - check connection string and database availability");
            }

            if (!healthCheck.DocumentsTableAccessible)
            {
                recommendations.Add("Documents table not accessible - run database initialization");
            }

            if (healthCheck.SyncStatus == "Out of Sync")
            {
                recommendations.Add("File metadata and documents are out of sync - run sync operation");
            }

            if (stats.PendingDocuments > 0)
            {
                recommendations.Add($"{stats.PendingDocuments} documents are pending processing by Python service");
            }

            if (stats.SyncedPercentage < 100)
            {
                recommendations.Add($"Only {stats.SyncedPercentage:F1}% of files are synced to documents - consider running sync");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("System is healthy and fully operational");
            }

            return recommendations;
        }
    }

    /// <summary>
    /// Request model for updating processing status
    /// </summary>
    public class UpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}