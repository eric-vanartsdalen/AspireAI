using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data
{
    /// <summary>
    /// Unified datasource entity that tracks the complete lifecycle:
    /// Upload ? Docling Processing ? Page Extraction ? Neo4j Integration
    /// 
    /// Replaces the previous Files/Documents dual-table design with a single source of truth.
    /// </summary>
    [Table("datasources")]
    public class FileMetadata
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        // ==================== Core File Identification ====================
        
        [Required]
        [Column("source_name")]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [Column("original_source_name")]
        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required]
        [Column("source_path")]
        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Column("source_hash")]
        [MaxLength(64)]
        public string FileHash { get; set; } = string.Empty;

        // ==================== File Metadata ====================
        
        [Column("source_size")]
        public long FileSize { get; set; } = 0;

        [Column("mime_type")]
        [MaxLength(100)]
        public string? MimeType { get; set; }

        // ==================== Upload Tracking ====================
        
        [Column("ingested_at")]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // ==================== Processing Lifecycle ====================
        
        /// <summary>
        /// Processing status: 'uploaded' | 'processing' | 'processed' | 'error'
        /// </summary>
        [Required]
        [Column("status")]
        [MaxLength(50)]
        public string Status { get; set; } = "uploaded";

        [Column("processing_started_at")]
        public DateTime? ProcessingStartedAt { get; set; }

        [Column("processing_completed_at")]
        public DateTime? ProcessingCompletedAt { get; set; }

        [Column("processing_error")]
        public string? ProcessingError { get; set; }

        // ==================== Docling Processing Output ====================
        
        [Column("docling_document_path")]
        [MaxLength(500)]
        public string? DoclingDocumentPath { get; set; }

        [Column("total_pages")]
        public int? TotalPages { get; set; }

        // ==================== Neo4j Integration (Future) ====================
        
        [Column("neo4j_document_node_id")]
        [MaxLength(100)]
        public string? Neo4jDocumentNodeId { get; set; }

        // ==================== Future Extensibility ====================
        
        /// <summary>
        /// Source type for future features: 'upload' (default), 'website', etc.
        /// </summary>
        [Column("source_type")]
        [MaxLength(50)]
        public string SourceType { get; set; } = "upload";

        [Column("source_url")]
        [MaxLength(500)]
        public string? SourceUrl { get; set; }

        // ==================== Navigation Properties ====================
        
        public virtual ICollection<DocumentPage> Pages { get; set; } = new List<DocumentPage>();

        // ==================== Computed Properties ====================
        
        /// <summary>
        /// Gets a short representation of the file hash for display purposes
        /// </summary>
        [NotMapped]
        public string ShortHash => string.IsNullOrEmpty(FileHash) ? "-" : FileHash.Substring(0, Math.Min(8, FileHash.Length));

        /// <summary>
        /// Checks if this file has a valid hash
        /// </summary>
        [NotMapped]
        public bool HasHash => !string.IsNullOrEmpty(FileHash);

        /// <summary>
        /// Gets a formatted status display
        /// </summary>
        [NotMapped]
        public string StatusDisplay => Status.ToUpperInvariant();

        /// <summary>
        /// Checks if the file has been processed
        /// </summary>
        [NotMapped]
        public bool IsProcessed => Status == "processed";

        /// <summary>
        /// Checks if the file is currently being processed
        /// </summary>
        [NotMapped]
        public bool IsProcessing => Status == "processing";

        /// <summary>
        /// Checks if processing failed
        /// </summary>
        [NotMapped]
        public bool HasError => Status == "error";

        /// <summary>
        /// Checks if the file is ready for processing
        /// </summary>
        [NotMapped]
        public bool IsReadyForProcessing => Status == "uploaded";
    }

    /// <summary>
    /// Document page entity for storing page-level content extracted by docling.
    /// Enables page-by-page RAG retrieval and Neo4j graph integration.
    /// </summary>
    [Table("datasource_pages")]
    public class DocumentPage
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("datasource_id")]
        public int FileId { get; set; }

        [Column("page_number")]
        public int PageNumber { get; set; }

        [Required]
        [Column("content")]
        public string Content { get; set; } = string.Empty;

        [Column("page_metadata")]
        public string? PageMetadata { get; set; }

        [Column("neo4j_page_node_id")]
        [MaxLength(100)]
        public string? Neo4jPageNodeId { get; set; }

        // ==================== Navigation Properties ====================
        
        [ForeignKey("FileId")]
        public virtual FileMetadata File { get; set; } = null!;
    }

    // ==================== Legacy Compatibility (DEPRECATED) ====================
    // These entities are maintained for backward compatibility during migration
    // TODO: Remove after all code is updated to use FileMetadata directly

    [Obsolete("Use FileMetadata instead. This entity exists only for backward compatibility.")]
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

        public virtual ICollection<ProcessedDocument> ProcessedDocuments { get; set; } = new List<ProcessedDocument>();
    }

    [Obsolete("Use FileMetadata.DoclingDocumentPath instead. This entity exists only for backward compatibility.")]
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
        public string? ProcessingMetadata { get; set; }

        [Column("neo4j_node_id")]
        [MaxLength(100)]
        public string? Neo4jNodeId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; } = null!;

        public virtual ICollection<DocumentPage> DocumentPages { get; set; } = new List<DocumentPage>();
    }
}