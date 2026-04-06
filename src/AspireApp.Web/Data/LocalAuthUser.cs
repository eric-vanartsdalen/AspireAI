using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AspireApp.Web.Data;

[Table("local_auth_users")]
public sealed class LocalAuthUser
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("username")]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [Column("normalized_username")]
    [MaxLength(100)]
    public string NormalizedUsername { get; set; } = string.Empty;

    [Required]
    [Column("email")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Column("normalized_email")]
    [MaxLength(256)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [Required]
    [Column("display_name")]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [Column("default_tenant_id")]
    [MaxLength(100)]
    public string DefaultTenantId { get; set; } = "default";

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
