namespace AspireApp.Web.Services;

/// <summary>
/// Scoped service for managing tenant context in Blazor sessions.
/// Provides tenant isolation without full authentication infrastructure.
/// </summary>
public sealed class TenantContextService(
    TenantManagementService tenantManagementService,
    AuthenticationContext authenticationContext)
{
    public const string DefaultTenantId = "default";

    private readonly TenantManagementService _tenantManagementService = tenantManagementService;
    private readonly AuthenticationContext _authenticationContext = authenticationContext;
    private readonly List<TenantSummary> _tenants = [];

    private string _currentTenantId = DefaultTenantId;
    private string? _currentUserId;

    /// <summary>
    /// Gets or sets the current tenant ID for this session.
    /// </summary>
    public string CurrentTenantId
    {
        get => _currentTenantId;
        set => SetCurrentTenant(value);
    }

    public TenantSummary? CurrentTenant =>
        _tenants.FirstOrDefault(tenant => tenant.TenantId.Equals(_currentTenantId, StringComparison.OrdinalIgnoreCase));

    public string CurrentTenantName =>
        CurrentTenant?.Name ?? _currentTenantId;

    public bool CurrentTenantIsProtected => CurrentTenant?.IsProtected ?? false;

    public IReadOnlyList<TenantSummary> Tenants => _tenants;

    /// <summary>
    /// Event raised when the tenant context changes.
    /// </summary>
    public event Action? OnTenantChanged;

    /// <summary>
    /// Event raised when the tenant list changes.
    /// </summary>
    public event Action? OnTenantListChanged;

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_tenants.Count == 0)
        {
            await InitializeForUserAsync(_authenticationContext.CurrentUser, cancellationToken);
        }
    }

    public async Task InitializeForUserAsync(AuthenticatedUser? user, CancellationToken cancellationToken = default)
    {
        if (user is null)
        {
            Reset();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentUserId) &&
            string.Equals(_currentUserId, user.UserId, StringComparison.OrdinalIgnoreCase) &&
            _tenants.Count > 0)
        {
            return;
        }

        _currentUserId = user.UserId;
        var accessSnapshot = await _tenantManagementService.EnsureTenantAccessAsync(
            new TenantUserDescriptor(user.UserId, user.DisplayName, user.Email),
            cancellationToken);

        UpdateTenants(accessSnapshot);
    }

    public void Reset()
    {
        _currentUserId = null;
        _tenants.Clear();
        SetCurrentTenant(DefaultTenantId, allowUnknown: true);
        OnTenantListChanged?.Invoke();
    }

    public async Task<TenantSummary?> CreateTenantAsync(string tenantName, CancellationToken cancellationToken = default)
    {
        var currentUser = _authenticationContext.CurrentUser;
        if (currentUser is null)
        {
            return null;
        }

        var createdTenant = await _tenantManagementService.CreateTenantAsync(
            currentUser.UserId,
            tenantName,
            cancellationToken);

        if (createdTenant is null)
        {
            return null;
        }

        await RefreshAsync(cancellationToken);
        SetCurrentTenant(createdTenant.TenantId, allowUnknown: true);
        return createdTenant;
    }

    public async Task<bool> RenameTenantAsync(string tenantId, string tenantName, CancellationToken cancellationToken = default)
    {
        var currentUser = _authenticationContext.CurrentUser;
        if (currentUser is null)
        {
            return false;
        }

        var renamed = await _tenantManagementService.RenameTenantAsync(
            currentUser.UserId,
            tenantId,
            tenantName,
            cancellationToken);

        if (!renamed)
        {
            return false;
        }

        await RefreshAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var currentUser = _authenticationContext.CurrentUser;
        if (currentUser is null)
        {
            return false;
        }

        var deleted = await _tenantManagementService.DeleteTenantAsync(
            currentUser.UserId,
            tenantId,
            cancellationToken);

        if (!deleted)
        {
            return false;
        }

        await RefreshAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryAddMemberAsync(string tenantId, string username, CancellationToken cancellationToken = default)
    {
        var currentUser = _authenticationContext.CurrentUser;
        if (currentUser is null)
        {
            return false;
        }

        return await _tenantManagementService.AddMemberByUsernameAsync(
            currentUser.UserId,
            tenantId,
            username,
            cancellationToken);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var currentUser = _authenticationContext.CurrentUser;
        if (currentUser is null)
        {
            Reset();
            return;
        }

        var tenants = await _tenantManagementService.GetTenantsAsync(currentUser.UserId, cancellationToken);
        var defaultTenantId = tenants.FirstOrDefault(tenant => tenant.IsDefault)?.TenantId ?? DefaultTenantId;
        UpdateTenants(new TenantAccessSnapshot(defaultTenantId, tenants));
    }

    private void UpdateTenants(TenantAccessSnapshot accessSnapshot)
    {
        var previousTenantId = _currentTenantId;
        _tenants.Clear();
        _tenants.AddRange(accessSnapshot.Tenants);
        OnTenantListChanged?.Invoke();

        var resolvedTenantId = accessSnapshot.DefaultTenantId;
        if (!string.IsNullOrWhiteSpace(previousTenantId) &&
            _tenants.Any(tenant => tenant.TenantId.Equals(previousTenantId, StringComparison.OrdinalIgnoreCase)))
        {
            resolvedTenantId = previousTenantId;
        }

        SetCurrentTenant(resolvedTenantId, allowUnknown: true);
    }

    private void SetCurrentTenant(string value, bool allowUnknown = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(value));
        }

        if (!allowUnknown &&
            _tenants.Count > 0 &&
            _tenants.All(tenant => !tenant.TenantId.Equals(value, StringComparison.OrdinalIgnoreCase)))
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
