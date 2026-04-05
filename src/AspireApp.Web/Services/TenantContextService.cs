namespace AspireApp.Web.Services;

/// <summary>
/// Scoped service for managing tenant context in Blazor sessions.
/// Provides tenant isolation without full authentication infrastructure.
/// </summary>
public class TenantContextService
{
    private string _currentTenantId = "default";

    /// <summary>
    /// Gets or sets the current tenant ID for this session.
    /// </summary>
    public string CurrentTenantId
    {
        get => _currentTenantId;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Tenant ID cannot be empty.", nameof(value));
            }
            _currentTenantId = value;
            OnTenantChanged?.Invoke();
        }
    }

    /// <summary>
    /// Event raised when the tenant context changes.
    /// </summary>
    public event Action? OnTenantChanged;

    /// <summary>
    /// Gets a list of available tenant IDs (hardcoded for Phase 1).
    /// TODO: Replace with database lookup in Phase 6.
    /// </summary>
    public static List<string> GetAvailableTenants()
    {
        return new List<string>
        {
            "default",
            "tenant-a",
            "tenant-b",
            "demo"
        };
    }
}
