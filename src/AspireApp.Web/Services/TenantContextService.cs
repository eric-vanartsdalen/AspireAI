namespace AspireApp.Web.Services;

/// <summary>
/// Scoped service for managing tenant context in Blazor sessions.
/// Provides tenant isolation without full authentication infrastructure.
/// </summary>
public class TenantContextService
{
    public const string DefaultTenantId = "default";
    private static readonly string[] AvailableTenantIds =
    [
        DefaultTenantId,
        "tenant-a",
        "tenant-b",
        "demo"
    ];
    private static readonly HashSet<string> AvailableTenantLookup = new(AvailableTenantIds, StringComparer.OrdinalIgnoreCase);

    private string _currentTenantId = DefaultTenantId;

    /// <summary>
    /// Gets or sets the current tenant ID for this session.
    /// </summary>
    public string CurrentTenantId
    {
        get => _currentTenantId;
        set => SetCurrentTenant(value);
    }

    /// <summary>
    /// Event raised when the tenant context changes.
    /// </summary>
    public event Action? OnTenantChanged;

    public void InitializeForUser(string tenantId)
    {
        SetCurrentTenant(tenantId);
    }

    public void Reset()
    {
        SetCurrentTenant(DefaultTenantId);
    }

    /// <summary>
    /// Gets a list of available tenant IDs (hardcoded for Phase 1).
    /// TODO: Replace with database lookup in Phase 6.
    /// </summary>
    public static List<string> GetAvailableTenants()
    {
        return [.. AvailableTenantIds];
    }

    private void SetCurrentTenant(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(value));
        }

        if (!AvailableTenantLookup.Contains(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Tenant ID '{value}' is not recognized by the current workspace configuration.");
        }

        if (string.Equals(_currentTenantId, value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentTenantId = value;
        OnTenantChanged?.Invoke();
    }
}
