using System;

namespace AspireApp.Web.Data
{
    public class DocumentPage
    {
        public int Id { get; set; }
        public int ProcessedDocumentId { get; set; }
        public ProcessedDocument ProcessedDocument { get; set; } = null!;
        public int PageNumber { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? Neo4jNodeId { get; set; }
    }
}