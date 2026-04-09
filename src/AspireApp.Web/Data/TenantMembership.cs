using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data;

[Table("tenant_memberships")]
public sealed class TenantMembership
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("tenant_id")]
    [MaxLength(100)]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    [Column("user_id")]
    [MaxLength(200)]
    public string UserId { get; set; } = string.Empty;

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("TenantId")]
    public Tenant Tenant { get; set; } = null!;
}
