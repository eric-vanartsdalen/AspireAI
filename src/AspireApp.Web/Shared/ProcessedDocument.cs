using System;
using System.Collections.Generic;

namespace AspireApp.Web.Data
{
    public class ProcessedDocument
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public Document Document { get; set; } = null!;
        public string DoclingDocumentPath { get; set; } = string.Empty;
        public string? Neo4jNodeId { get; set; }
        public DateTime ProcessingDate { get; set; }
        public ICollection<DocumentPage> DocumentPages { get; set; } = new List<DocumentPage>();
    }
}