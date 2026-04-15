using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data;

[Table("chat_conversations")]
public sealed class ChatConversation
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [Column("owner_user_id")]
    [MaxLength(200)]
    public string OwnerUserId { get; set; } = string.Empty;

    [Column("tenant_id")]
    [MaxLength(100)]
    public string? TenantId { get; set; }

    [Required]
    [Column("title")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("title_source")]
    [MaxLength(20)]
    public string TitleSource { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_message_at")]
    public DateTime? LastMessageAt { get; set; }

    [Required]
    [Column("chat_mode")]
    [MaxLength(20)]
    public string ChatMode { get; set; } = "regular";

    public ICollection<ChatConversationMessage> Messages { get; set; } = [];
}

[Table("chat_messages")]
public sealed class ChatConversationMessage
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("conversation_id")]
    public Guid ConversationId { get; set; }

    [Required]
    [Column("owner_user_id")]
    [MaxLength(200)]
    public string OwnerUserId { get; set; } = string.Empty;

    [Required]
    [Column("role")]
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("sequence")]
    public int Sequence { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ConversationId))]
    public ChatConversation Conversation { get; set; } = null!;
}
