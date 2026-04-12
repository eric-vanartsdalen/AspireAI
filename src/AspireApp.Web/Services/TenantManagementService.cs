using AspireApp.Web.Data;
using AspireApp.Web.Shared;
using Microsoft.EntityFrameworkCore;

namespace AspireApp.Web.Services;

public sealed record TenantUserDescriptor(string UserId, string DisplayName, string Email);

public sealed record TenantSummary(
    string TenantId,
    string Name,
    bool IsDefault,
    bool IsProtected,
    bool IsOwner);

public sealed record TenantAccessSnapshot(
    string DefaultTenantId,
    IReadOnlyList<TenantSummary> Tenants);

public sealed class TenantManagementService(
    UploadDbContext dbContext,
    ILogger<TenantManagementService> logger)
{
    private const int TenantIdSuffixLength = 12;

    private readonly UploadDbContext _dbContext = dbContext;
    private readonly ILogger<TenantManagementService> _logger = logger;

    public async Task<TenantAccessSnapshot> EnsureTenantAccessAsync(
        TenantUserDescriptor user,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.UserId))
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(user));
        }

        var memberships = await LoadMembershipsAsync(user.UserId, cancellationToken);
        if (memberships.Count == 0)
        {
            await CreateDefaultTenantAsync(user, cancellationToken);
            memberships = await LoadMembershipsAsync(user.UserId, cancellationToken);
        }

        var defaultMemberships = memberships.Where(membership => membership.IsDefault).ToList();
        if (defaultMemberships.Count == 0)
        {
            await CreateDefaultTenantAsync(user, cancellationToken);
            memberships = await LoadMembershipsAsync(user.UserId, cancellationToken);
        }
        else if (defaultMemberships.Count > 1)
        {
            var keepDefault = defaultMemberships
                .OrderByDescending(membership => membership.Tenant.IsProtected)
                .ThenBy(membership => membership.CreatedAt)
                .First();

            foreach (var membership in defaultMemberships.Where(membership => membership != keepDefault))
            {
                membership.IsDefault = false;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            memberships = await LoadMembershipsAsync(user.UserId, cancellationToken);
        }
        else
        {
            var defaultMembership = defaultMemberships[0];
            if (!defaultMembership.Tenant.IsProtected)
            {
                defaultMembership.Tenant.IsProtected = true;
                defaultMembership.Tenant.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                memberships = await LoadMembershipsAsync(user.UserId, cancellationToken);
            }
        }

        var summaries = memberships
            .Select(membership => ToSummary(membership, user.UserId))
            .OrderByDescending(summary => summary.IsDefault)
            .ThenBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var defaultTenantId = summaries.FirstOrDefault(summary => summary.IsDefault)?.TenantId ?? TenantContextService.DefaultTenantId;
        return new TenantAccessSnapshot(defaultTenantId, summaries);
    }

    public async Task<IReadOnlyList<TenantSummary>> GetTenantsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var memberships = await _dbContext.TenantMemberships
            .AsNoTracking()
            .Include(membership => membership.Tenant)
            .Where(membership => membership.UserId == userId)
            .ToListAsync(cancellationToken);

        return memberships
            .Select(membership => ToSummary(membership, userId))
            .OrderByDescending(summary => summary.IsDefault)
            .ThenBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TenantSummary?> CreateTenantAsync(
        string userId,
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var normalizedName = NormalizeTenantName(tenantName);
        if (normalizedName is null)
        {
            return null;
        }

        var timestamp = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = CreateTenantId(),
            Name = normalizedName,
            OwnerUserId = userId,
            IsProtected = false,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        _dbContext.Tenants.Add(tenant);
        _dbContext.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenant.Id,
            UserId = userId,
            IsDefault = false,
            CreatedAt = timestamp
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Tenant {TenantId} created for user {UserId}", tenant.Id, userId);
        }

        return new TenantSummary(tenant.Id, tenant.Name, false, tenant.IsProtected, true);
    }

    public async Task<bool> RenameTenantAsync(
        string userId,
        string tenantId,
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeTenantName(tenantName);
        if (normalizedName is null)
        {
            return false;
        }

        var tenant = await _dbContext.Tenants.SingleOrDefaultAsync(
            existing => existing.Id == tenantId,
            cancellationToken);

        if (tenant is null ||
            !string.Equals(tenant.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        tenant.Name = normalizedName;
        tenant.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteTenantAsync(
        string userId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants.SingleOrDefaultAsync(
            existing => existing.Id == tenantId,
            cancellationToken);

        if (tenant is null ||
            tenant.IsProtected ||
            !string.Equals(tenant.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _dbContext.Tenants.Remove(tenant);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddMemberByUsernameAsync(
        string userId,
        string tenantId,
        string username,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var tenant = await _dbContext.Tenants.SingleOrDefaultAsync(
            existing => existing.Id == tenantId,
            cancellationToken);

        if (tenant is null ||
            !string.Equals(tenant.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cleanedUsername = LocalAuthValueNormalizer.Clean(username);
        if (string.IsNullOrWhiteSpace(cleanedUsername))
        {
            return false;
        }

        var normalizedUsername = LocalAuthValueNormalizer.Normalize(cleanedUsername);
        var user = await _dbContext.LocalAuthUsers.SingleOrDefaultAsync(
            existing => existing.IsActive && existing.NormalizedUsername == normalizedUsername,
            cancellationToken);

        if (user is null)
        {
            return false;
        }

        var targetUserId = BuildLocalUserId(user.Id);
        if (string.Equals(targetUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var alreadyMember = await _dbContext.TenantMemberships.AnyAsync(
            membership => membership.TenantId == tenantId && membership.UserId == targetUserId,
            cancellationToken);

        if (alreadyMember)
        {
            return false;
        }

        var membership = new TenantMembership
        {
            TenantId = tenantId,
            UserId = targetUserId,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.TenantMemberships.Add(membership);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _dbContext.Entry(membership).State = EntityState.Detached;
            _logger.LogWarning(ex, "Save failed while adding tenant member to {TenantId}", tenantId);
            return false;
        }
    }

    private static string BuildLocalUserId(int userId) => $"local-{userId}";

    private async Task<List<TenantMembership>> LoadMembershipsAsync(string userId, CancellationToken cancellationToken)
    {
        return await _dbContext.TenantMemberships
            .Include(membership => membership.Tenant)
            .Where(membership => membership.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    private async Task CreateDefaultTenantAsync(TenantUserDescriptor user, CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = CreateTenantId(),
            Name = BuildDefaultTenantName(user),
            OwnerUserId = user.UserId,
            IsProtected = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        _dbContext.Tenants.Add(tenant);
        _dbContext.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenant.Id,
            UserId = user.UserId,
            IsDefault = true,
            CreatedAt = timestamp
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildDefaultTenantName(TenantUserDescriptor user)
    {
        var baseName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Email
            : user.DisplayName;

        if (string.IsNullOrWhiteSpace(baseName))
        {
            return "Personal workspace";
        }

        var trimmed = baseName.Trim();
        var name = $"{trimmed}'s workspace";
        return name.Length <= 200 ? name : name[..200];
    }

    private static string? NormalizeTenantName(string tenantName)
    {
        if (string.IsNullOrWhiteSpace(tenantName))
        {
            return null;
        }

        var trimmed = tenantName.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }

    private static string CreateTenantId()
    {
        var suffix = Guid.NewGuid().ToString("N")[..TenantIdSuffixLength];
        return $"tenant-{suffix}";
    }

    private static TenantSummary ToSummary(TenantMembership membership, string userId)
    {
        var tenant = membership.Tenant;
        return new TenantSummary(
            tenant.Id,
            tenant.Name,
            membership.IsDefault,
            tenant.IsProtected,
            string.Equals(tenant.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase));
    }
}
