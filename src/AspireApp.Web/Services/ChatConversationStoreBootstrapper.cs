using AspireApp.Web.Shared;
using Microsoft.EntityFrameworkCore;

namespace AspireApp.Web.Services;

/// <summary>
/// Ensures persisted chat history tables exist in PostgreSQL-backed operational stores.
/// </summary>
public sealed class ChatConversationStoreBootstrapper(UploadDbContext dbContext)
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly UploadDbContext _dbContext = dbContext;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(_dbContext.Database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal))
        {
            await EnsureSchemaAsync(cancellationToken);
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS chat_conversations (
                id uuid PRIMARY KEY,
                owner_user_id character varying(200) NOT NULL,
                tenant_id character varying(100) NULL,
                title character varying(200) NOT NULL,
                title_source character varying(20) NOT NULL DEFAULT 'fallback',
                created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                updated_at timestamp with time zone NOT NULL DEFAULT NOW(),
                last_message_at timestamp with time zone NULL
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS idx_chat_conversations_owner_updated
            ON chat_conversations (owner_user_id, updated_at DESC);
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS idx_chat_conversations_tenant
            ON chat_conversations (tenant_id);
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS chat_messages (
                id uuid PRIMARY KEY,
                conversation_id uuid NOT NULL,
                owner_user_id character varying(200) NOT NULL,
                role character varying(20) NOT NULL,
                content text NOT NULL,
                sequence integer NOT NULL,
                created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                CONSTRAINT fk_chat_messages_conversation
                    FOREIGN KEY (conversation_id)
                    REFERENCES chat_conversations(id)
                    ON DELETE CASCADE
                    ON UPDATE CASCADE
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_chat_messages_conversation_sequence
            ON chat_messages (conversation_id, sequence);
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS idx_chat_messages_owner
            ON chat_messages (owner_user_id);
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS idx_chat_messages_conversation_created
            ON chat_messages (conversation_id, created_at);
            """,
            cancellationToken);
    }
}
