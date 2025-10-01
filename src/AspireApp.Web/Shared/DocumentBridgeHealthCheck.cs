using System;
using AspireApp.Web.Shared;

namespace AspireApp.Web.Shared
{
    public class DocumentBridgeHealthCheck
    {
        public bool OverallHealthy { get; set; }
        public bool DatabaseConnected { get; set; }
        public bool DocumentsTableAccessible { get; set; }
        public string SyncStatus { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

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

    public class SyncResult
    {
        public bool Success { get; set; }
        public int FilesSyncedToDocuments { get; set; }
        public int DocumentsSyncedToFiles { get; set; }
        public SyncStatus? FinalStatus { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

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