using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data;

[Table("tenants")]
public sealed class Tenant
{
    [Key]
    [Column("id")]
    [MaxLength(100)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("owner_user_id")]
    [MaxLength(200)]
    public string OwnerUserId { get; set; } = string.Empty;

    [Column("is_protected")]
    public bool IsProtected { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TenantMembership> Memberships { get; set; } = [];
}
