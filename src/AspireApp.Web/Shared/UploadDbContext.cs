using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data
{
	public class UploadDbContext : DbContext
    {
        public UploadDbContext(DbContextOptions<UploadDbContext> options) : base(options) { }
        
        // ==================== Primary Schema ====================
        
        /// <summary>
        /// Unified datasources table - single source of truth for datasource lifecycle
        /// </summary>
        public DbSet<FileMetadata> Datasources => Set<FileMetadata>();
        
        /// <summary>
        /// Datasource pages for RAG retrieval
        /// </summary>
        public DbSet<DocumentPage> DatasourcePages => Set<DocumentPage>();

        // Backward compatibility alias
        [Obsolete("Use Datasources DbSet instead")]
        public DbSet<FileMetadata> Files => Set<FileMetadata>();

        // ==================== Legacy Schema (Backward Compatibility) ====================
        
        /// <summary>
        /// Legacy documents table - marked obsolete, use Datasources instead
        /// </summary>
        [Obsolete("Use Datasources DbSet instead")]
        public DbSet<Document> Documents => Set<Document>();
        
        /// <summary>
        /// Legacy processed documents table - marked obsolete
        /// </summary>
        [Obsolete("Use Datasources.DoclingDocumentPath instead")]
        public DbSet<ProcessedDocument> ProcessedDocuments => Set<ProcessedDocument>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==================== Primary Schema Configuration ====================

            // Configure Datasources entity
            modelBuilder.Entity<FileMetadata>(entity =>
            {
                entity.ToTable("datasources");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                
                // Core identification
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(255).HasColumnName("source_name");
                entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255).HasColumnName("original_source_name");
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500).HasColumnName("source_path");
                entity.Property(e => e.FileHash).HasMaxLength(64).HasDefaultValue(string.Empty).HasColumnName("source_hash");
                
                // Metadata
                entity.Property(e => e.FileSize).HasDefaultValue(0).HasColumnName("source_size");
                entity.Property(e => e.MimeType).HasMaxLength(100);
                
                // Lifecycle tracking
                entity.Property(e => e.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("ingested_at");
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50).HasDefaultValue("uploaded");
                
                // Docling output
                entity.Property(e => e.DoclingDocumentPath).HasMaxLength(500);
                
                // Neo4j integration
                entity.Property(e => e.Neo4jDocumentNodeId).HasMaxLength(100);
                
                // Future extensibility
                entity.Property(e => e.SourceType).HasMaxLength(50).HasDefaultValue("upload");
                entity.Property(e => e.SourceUrl).HasMaxLength(500);

                // Indexes for performance
                entity.HasIndex(e => e.Status).HasDatabaseName("idx_datasources_status");
                entity.HasIndex(e => e.FileHash).HasDatabaseName("idx_datasources_hash");
                entity.HasIndex(e => e.UploadedAt).HasDatabaseName("idx_datasources_ingested");
                entity.HasIndex(e => e.SourceType).HasDatabaseName("idx_datasources_type");

                // Relationships
                entity.HasMany(e => e.Pages)
                      .WithOne(p => p.File)
                      .HasForeignKey(p => p.FileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure DatasourcePages entity
            modelBuilder.Entity<DocumentPage>(entity =>
            {
                entity.ToTable("datasource_pages");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.FileId).HasColumnName("datasource_id");
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.Neo4jPageNodeId).HasMaxLength(100);

                // Unique constraint on datasource_id + page_number
                entity.HasIndex(e => new { e.FileId, e.PageNumber })
                      .IsUnique()
                      .HasDatabaseName("idx_pages_datasource_page");

                // Indexes for performance
                entity.HasIndex(e => e.FileId).HasDatabaseName("idx_pages_datasource_id");
            });

            // ==================== Legacy Schema Configuration (Backward Compatibility) ====================

            // Configure legacy Documents entity
#pragma warning disable CS0618 // Legacy Document entity is kept for backward compatibility
            modelBuilder.Entity<Document>(entity =>
            {
                entity.ToTable("documents");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
                entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
                entity.Property(e => e.MimeType).HasMaxLength(100);
                entity.Property(e => e.ProcessingStatus).HasMaxLength(50).HasDefaultValue("pending");
                entity.Property(e => e.Processed).HasDefaultValue(false);
                entity.Property(e => e.UploadDate).HasDefaultValueSql("CURRENT_TIMESTAMP");

                // Legacy indexes
                entity.HasIndex(e => e.Processed).HasDatabaseName("idx_documents_processed");
                entity.HasIndex(e => e.UploadDate).HasDatabaseName("idx_documents_upload_date");
                entity.HasIndex(e => e.ProcessingStatus).HasDatabaseName("idx_documents_status");
                entity.HasIndex(e => e.FileName).HasDatabaseName("idx_documents_filename");
            });

            // Configure legacy ProcessedDocuments entity
            modelBuilder.Entity<ProcessedDocument>(entity =>
            {
                entity.ToTable("processed_documents");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.DoclingDocumentPath).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Neo4jNodeId).HasMaxLength(100);
                entity.Property(e => e.ProcessingDate).HasDefaultValueSql("CURRENT_TIMESTAMP");

                // Foreign key relationship (legacy)
                entity.HasOne(e => e.Document)
                      .WithMany(d => d.ProcessedDocuments)
                      .HasForeignKey(e => e.DocumentId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Legacy indexes
                entity.HasIndex(e => e.DocumentId).HasDatabaseName("idx_processed_documents_document_id");
            });
#pragma warning restore CS0618
        }
    }
}
