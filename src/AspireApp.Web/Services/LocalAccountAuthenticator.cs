using AspireApp.Web.Data;
using AspireApp.Web.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AspireApp.Web.Services;

/// <summary>
/// Verifies managed local credentials against the operational store.
/// </summary>
public sealed class LocalAccountAuthenticator(
    UploadDbContext dbContext,
    IPasswordHasher<LocalAuthUser> passwordHasher,
    IOptions<LocalAuthenticationOptions> options,
    TenantManagementService tenantManagementService)
{
    private static readonly Regex ValidUsernamePattern = new("^[A-Za-z0-9._-]{3,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly UploadDbContext _dbContext = dbContext;
    private readonly IPasswordHasher<LocalAuthUser> _passwordHasher = passwordHasher;
    private readonly LocalAuthenticationOptions _options = options.Value;
    private readonly TenantManagementService _tenantManagementService = tenantManagementService;

    public async Task<AuthenticatedUser?> AuthenticateAsync(string identifier, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled ||
            string.IsNullOrWhiteSpace(identifier) ||
            string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var cleanedIdentifier = LocalAuthValueNormalizer.Clean(identifier);
        if (password.Length < LocalAuthenticationOptions.MinimumPasswordLength)
        {
            return null;
        }

        var normalizedIdentifier = LocalAuthValueNormalizer.Normalize(cleanedIdentifier);
        var user = await _dbContext.LocalAuthUsers.SingleOrDefaultAsync(
            value => value.IsActive &&
                     (value.NormalizedUsername == normalizedIdentifier || value.NormalizedEmail == normalizedIdentifier),
            cancellationToken);

        if (user is null)
        {
            user = await TryCreateUserAsync(cleanedIdentifier, normalizedIdentifier, password, cancellationToken);
        }

        if (user is null)
        {
            return null;
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var tenantSnapshot = await _tenantManagementService.EnsureTenantAccessAsync(
            new TenantUserDescriptor($"local-{user.Id}", user.DisplayName, user.Email),
            cancellationToken);

        var needsSave = false;
        if (!string.Equals(user.DefaultTenantId, tenantSnapshot.DefaultTenantId, StringComparison.OrdinalIgnoreCase))
        {
            user.DefaultTenantId = tenantSnapshot.DefaultTenantId;
            needsSave = true;
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            user.UpdatedAt = DateTime.UtcNow;
            needsSave = true;
        }

        if (needsSave)
        {
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AuthenticatedUser(
            $"local-{user.Id}",
            user.DisplayName,
            user.Email,
            LocalAuthService.ProviderId,
            LocalAuthService.ProviderDisplayName,
            tenantSnapshot.DefaultTenantId);
    }

    private async Task<LocalAuthUser?> TryCreateUserAsync(
        string cleanedIdentifier,
        string normalizedIdentifier,
        string password,
        CancellationToken cancellationToken)
    {
        if (!_options.AllowSelfRegistration ||
            IsEmailIdentifier(cleanedIdentifier) ||
            !ValidUsernamePattern.IsMatch(cleanedIdentifier))
        {
            return null;
        }

        var timestamp = DateTime.UtcNow;
        var syntheticEmail = $"{normalizedIdentifier}@local.aspireai";
        var user = new LocalAuthUser
        {
            Username = cleanedIdentifier,
            NormalizedUsername = normalizedIdentifier,
            Email = syntheticEmail,
            NormalizedEmail = LocalAuthValueNormalizer.Normalize(syntheticEmail),
            DisplayName = cleanedIdentifier,
            DefaultTenantId = string.Empty,
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        _dbContext.LocalAuthUsers.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(user).State = EntityState.Detached;
            return null;
        }
    }

    private static bool IsEmailIdentifier(string identifier) =>
        identifier.Contains('@');
}
