using System;
using System.Collections.Generic;

namespace AspireApp.Web.Data
{
    public class Document
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long? FileSize { get; set; }
        public string? MimeType { get; set; }
        public DateTime UploadDate { get; set; }
        public bool Processed { get; set; }
        public string ProcessingStatus { get; set; } = "pending";
        public ICollection<ProcessedDocument> ProcessedDocuments { get; set; } = new List<ProcessedDocument>();
    }
}