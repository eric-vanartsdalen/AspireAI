using AspireApp.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AspireApp.Web.Shared
{
    public class UploadDbContext(DbContextOptions<UploadDbContext> options) : DbContext(options)
    {
        // ==================== Primary Schema ====================

        /// <summary>
        /// Unified datasources table - single source of truth for datasource lifecycle
        /// </summary>
        public DbSet<FileMetadata> Datasources => Set<FileMetadata>();

        /// <summary>
        /// Datasource pages for RAG retrieval
        /// </summary>
        public DbSet<DocumentPage> DatasourcePages => Set<DocumentPage>();

        /// <summary>
        /// Managed local username/password accounts.
        /// </summary>
        public DbSet<LocalAuthUser> LocalAuthUsers => Set<LocalAuthUser>();

        /// <summary>
        /// Workspace tenants owned and shared across authenticated users.
        /// </summary>
        public DbSet<Tenant> Tenants => Set<Tenant>();

        /// <summary>
        /// Per-user tenant memberships and default tenant selections.
        /// </summary>
        public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

        /// <summary>
        /// Persisted chat conversations owned by individual authenticated users.
        /// </summary>
        public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();

        /// <summary>
        /// Persisted chat messages owned by individual authenticated users.
        /// </summary>
        public DbSet<ChatConversationMessage> ChatConversationMessages => Set<ChatConversationMessage>();

        // Backward compatibility alias
        [Obsolete("Use Datasources DbSet instead")]
        public DbSet<FileMetadata> Files => Set<FileMetadata>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==================== Primary Schema Configuration ====================

            // Configure Datasources entity
            modelBuilder.Entity<FileMetadata>(entity =>
            {
                entity.ToTable("files");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();

                // Note: Column names are defined via [Column] attributes in FileMetadata class
                // This matches the actual database schema with snake_case column names

                // Indexes for performance
                entity.HasIndex(e => e.Status).HasDatabaseName("idx_files_status");
                entity.HasIndex(e => e.FileHash).HasDatabaseName("idx_files_hash");
                entity.HasIndex(e => e.UploadedAt).HasDatabaseName("idx_files_uploaded");
                entity.HasIndex(e => e.TenantId).HasDatabaseName("idx_files_tenant");
                entity.HasIndex(e => new { e.TenantId, e.Status }).HasDatabaseName("idx_files_tenant_status");

                // Relationships
                entity.HasMany(e => e.Pages)
                      .WithOne(p => p.File)
                      .HasForeignKey(p => p.FileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure DatasourcePages entity
            modelBuilder.Entity<DocumentPage>(entity =>
            {
                entity.ToTable("document_pages");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();

                // Note: Column names are defined via [Column] attributes in DocumentPage class

                // Unique constraint on file_id + page_number
                entity.HasIndex(e => new { e.FileId, e.PageNumber })
                      .IsUnique()
                      .HasDatabaseName("idx_pages_document_page");

                // Indexes for performance
                entity.HasIndex(e => e.FileId).HasDatabaseName("idx_pages_file_id");
            });

            modelBuilder.Entity<LocalAuthUser>(entity =>
            {
                entity.ToTable("local_auth_users");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
                entity.Property(e => e.NormalizedUsername).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
                entity.Property(e => e.NormalizedEmail).IsRequired().HasMaxLength(256);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.DefaultTenantId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.IsActive).HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(e => e.NormalizedUsername)
                      .IsUnique()
                      .HasDatabaseName("ux_local_auth_users_normalized_username");

                entity.HasIndex(e => e.NormalizedEmail)
                      .IsUnique()
                      .HasDatabaseName("ux_local_auth_users_normalized_email");

                entity.HasIndex(e => e.IsActive)
                      .HasDatabaseName("idx_local_auth_users_is_active");

                entity.HasIndex(e => e.DefaultTenantId)
                      .HasDatabaseName("idx_local_auth_users_default_tenant");
            });

            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.ToTable("tenants");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(100);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.OwnerUserId).IsRequired().HasMaxLength(200);
                entity.Property(e => e.IsProtected).HasDefaultValue(false);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(e => e.OwnerUserId)
                      .HasDatabaseName("idx_tenants_owner_user");
            });

            modelBuilder.Entity<TenantMembership>(entity =>
            {
                entity.ToTable("tenant_memberships");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.TenantId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.UserId).IsRequired().HasMaxLength(200);
                entity.Property(e => e.IsDefault).HasDefaultValue(false);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(e => new { e.TenantId, e.UserId })
                      .IsUnique()
                      .HasDatabaseName("ux_tenant_memberships_tenant_user");

                entity.HasIndex(e => e.UserId)
                      .HasDatabaseName("idx_tenant_memberships_user");

                entity.HasIndex(e => e.TenantId)
                      .HasDatabaseName("idx_tenant_memberships_tenant");

                entity.HasOne(e => e.Tenant)
                      .WithMany(t => t.Memberships)
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChatConversation>(entity =>
            {
                entity.ToTable("chat_conversations");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.OwnerUserId).IsRequired().HasMaxLength(200);
                entity.Property(e => e.TenantId).HasMaxLength(100);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.TitleSource).IsRequired().HasMaxLength(20);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(e => new { e.OwnerUserId, e.UpdatedAt })
                      .HasDatabaseName("idx_chat_conversations_owner_updated");

                entity.HasIndex(e => e.TenantId)
                      .HasDatabaseName("idx_chat_conversations_tenant");

                entity.HasMany(e => e.Messages)
                      .WithOne(message => message.Conversation)
                      .HasForeignKey(message => message.ConversationId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChatConversationMessage>(entity =>
            {
                entity.ToTable("chat_messages");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.OwnerUserId).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Content).IsRequired();

                entity.HasIndex(e => new { e.ConversationId, e.Sequence })
                      .IsUnique()
                      .HasDatabaseName("ux_chat_messages_conversation_sequence");

                entity.HasIndex(e => e.OwnerUserId)
                      .HasDatabaseName("idx_chat_messages_owner");

                entity.HasIndex(e => new { e.ConversationId, e.CreatedAt })
                      .HasDatabaseName("idx_chat_messages_conversation_created");
            });
        }
    }
}
