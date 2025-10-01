using AspireApp.Web.Data;
using AspireApp.Web.Shared;

namespace AspireApp.Web.Shared
{
    public class DocumentProcessingStats
    {
        public int TotalDocuments { get; set; }
        public int PendingDocuments { get; set; }
        public int ProcessedDocuments { get; set; }
        public int ErrorDocuments { get; set; }
        public double SyncedPercentage { get; set; }
    }
}