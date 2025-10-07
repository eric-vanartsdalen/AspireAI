using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data
{
	public class UploadDbContext : DbContext
    {
        public UploadDbContext(DbContextOptions<UploadDbContext> options) : base(options) { }
        
        // Original file upload system
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
        // Python service compatible entities
        public DbSet<Document> Documents => Set<Document>();
        public DbSet<ProcessedDocument> ProcessedDocuments => Set<ProcessedDocument>();
        public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure the Documents entity
            modelBuilder.Entity<Document>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
                entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
                entity.Property(e => e.MimeType).HasMaxLength(100);
                entity.Property(e => e.ProcessingStatus).HasMaxLength(50).HasDefaultValue("pending");
                entity.Property(e => e.Processed).HasDefaultValue(false);
                entity.Property(e => e.UploadDate).HasDefaultValueSql("CURRENT_TIMESTAMP");

                // Index for performance
                entity.HasIndex(e => e.Processed);
                entity.HasIndex(e => e.UploadDate);
            });

            // Configure the ProcessedDocuments entity
            modelBuilder.Entity<ProcessedDocument>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.DoclingDocumentPath).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Neo4jNodeId).HasMaxLength(100);
                entity.Property(e => e.ProcessingDate).HasDefaultValueSql("CURRENT_TIMESTAMP");

                // Foreign key relationship
                entity.HasOne(e => e.Document)
                      .WithMany(d => d.ProcessedDocuments)
                      .HasForeignKey(e => e.DocumentId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Index for performance
                entity.HasIndex(e => e.DocumentId);
            });

            // Configure the DocumentPages entity
            modelBuilder.Entity<DocumentPage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.Neo4jNodeId).HasMaxLength(100);

                // Foreign key relationship
                entity.HasOne(e => e.ProcessedDocument)
                      .WithMany(pd => pd.DocumentPages)
                      .HasForeignKey(e => e.ProcessedDocumentId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Indexes for performance
                entity.HasIndex(e => e.ProcessedDocumentId);
                entity.HasIndex(e => e.PageNumber);
            });

            // Configure the original FileMetadata entity (if not already configured)
            modelBuilder.Entity<FileMetadata>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.FileName).IsRequired();
                entity.Property(e => e.Status).HasDefaultValue("Uploaded");
                entity.Property(e => e.FileHash).HasDefaultValue(string.Empty);
                
                // Index for performance
                entity.HasIndex(e => e.FileHash);
                entity.HasIndex(e => e.UploadedAt);
            });
        }
    }
}
