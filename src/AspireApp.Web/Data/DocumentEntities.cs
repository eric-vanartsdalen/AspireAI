using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data
{
    /// <summary>
    /// Entity that matches the Python service's expected 'documents' table schema
    /// This bridges the .NET file upload system with the Python document processing service
    /// </summary>
    [Table("documents")]
    public class Document
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("filename")]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [Column("original_filename")]
        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required]
        [Column("file_path")]
        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Column("file_size")]
        public long? FileSize { get; set; }

        [Column("mime_type")]
        [MaxLength(100)]
        public string? MimeType { get; set; }

        [Column("upload_date")]
        public DateTime UploadDate { get; set; } = DateTime.UtcNow;

        [Column("processed")]
        public bool Processed { get; set; } = false;

        [Column("processing_status")]
        [MaxLength(50)]
        public string ProcessingStatus { get; set; } = "pending";

        // Navigation properties
        public virtual ICollection<ProcessedDocument> ProcessedDocuments { get; set; } = new List<ProcessedDocument>();

        /// <summary>
        /// Creates a Document entity from a FileMetadata entity
        /// </summary>
        /// <param name="fileMetadata">The source FileMetadata</param>
        /// <param name="dataDirectory">The data directory path for constructing file path</param>
        /// <returns>A new Document entity</returns>
        public static Document FromFileMetadata(FileMetadata fileMetadata, string dataDirectory)
        {
            return new Document
            {
                FileName = fileMetadata.FileName,
                OriginalFileName = ExtractOriginalFileName(fileMetadata.FileName),
                FilePath = fileMetadata.FileName, // Store relative path
                FileSize = fileMetadata.Size,
                MimeType = GetMimeTypeFromFileName(fileMetadata.FileName),
                UploadDate = fileMetadata.UploadedAt,
                Processed = fileMetadata.Status == "Processed",
                ProcessingStatus = ConvertStatus(fileMetadata.Status)
            };
        }

        /// <summary>
        /// Extracts the original filename from the unique filename generated during upload
        /// </summary>
        private static string ExtractOriginalFileName(string uniqueFileName)
        {
            // Format: originalname_20240101_123456_abcd1234.ext
            // Extract the part before the first underscore followed by a date pattern
            var parts = uniqueFileName.Split('_');
            if (parts.Length >= 3)
            {
                // Check if second part looks like a date (8 digits)
                if (parts[1].Length == 8 && parts[1].All(char.IsDigit))
                {
                    return parts[0] + Path.GetExtension(uniqueFileName);
                }
            }
            
            // Fallback to the unique filename if pattern doesn't match
            return uniqueFileName;
        }

        /// <summary>
        /// Determines MIME type based on file extension
        /// </summary>
        private static string GetMimeTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Converts FileMetadata status to Document processing status
        /// </summary>
        private static string ConvertStatus(string fileMetadataStatus)
        {
            return fileMetadataStatus.ToLowerInvariant() switch
            {
                "uploaded" => "pending",
                "processed" => "completed",
                "error" => "error",
                "pending" => "pending",
                _ => "pending"
            };
        }
    }

    /// <summary>
    /// Entity that matches the Python service's expected 'processed_documents' table schema
    /// </summary>
    [Table("processed_documents")]
    public class ProcessedDocument
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("document_id")]
        public int DocumentId { get; set; }

        [Required]
        [Column("docling_document_path")]
        [MaxLength(500)]
        public string DoclingDocumentPath { get; set; } = string.Empty;

        [Column("total_pages")]
        public int? TotalPages { get; set; }

        [Column("processing_date")]
        public DateTime ProcessingDate { get; set; } = DateTime.UtcNow;

        [Column("processing_metadata")]
        public string? ProcessingMetadata { get; set; } // JSON string

        [Column("neo4j_node_id")]
        [MaxLength(100)]
        public string? Neo4jNodeId { get; set; }

        // Navigation properties
        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; } = null!;

        public virtual ICollection<DocumentPage> DocumentPages { get; set; } = new List<DocumentPage>();
    }

    /// <summary>
    /// Entity that matches the Python service's expected 'document_pages' table schema
    /// </summary>
    [Table("document_pages")]
    public class DocumentPage
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("processed_document_id")]
        public int ProcessedDocumentId { get; set; }

        [Column("page_number")]
        public int PageNumber { get; set; }

        [Required]
        [Column("content")]
        public string Content { get; set; } = string.Empty;

        [Column("page_metadata")]
        public string? PageMetadata { get; set; } // JSON string

        [Column("neo4j_node_id")]
        [MaxLength(100)]
        public string? Neo4jNodeId { get; set; }

        // Navigation properties
        [ForeignKey("ProcessedDocumentId")]
        public virtual ProcessedDocument ProcessedDocument { get; set; } = null!;
    }
}