using System.Security.Claims;

namespace AspireApp.Web.Services;

public static class AuthenticatedUserClaims
{
    public const string ProviderDisplayName = "provider_display_name";
    public const string TenantId = "tenant_id";

    public static void AddClaims(ClaimsIdentity identity, AuthenticatedUser user)
    {
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserId));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, user.ProviderId));
        identity.AddClaim(new Claim(ProviderDisplayName, user.ProviderDisplayName));
        identity.AddClaim(new Claim(TenantId, user.DefaultTenantId));
    }

    public static AuthenticatedUser? BuildUser(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                     principal.FindFirstValue("oid") ??
                     principal.FindFirstValue("sub");
        var displayName = principal.FindFirstValue(ClaimTypes.Name) ??
                          principal.FindFirstValue("name") ??
                          principal.Identity?.Name;
        var email = principal.FindFirstValue(ClaimTypes.Email) ??
                    principal.FindFirstValue("preferred_username") ??
                    principal.FindFirstValue("upn");
        var providerId = principal.FindFirstValue(ClaimTypes.AuthenticationMethod);
        var providerDisplayName = principal.FindFirstValue(ProviderDisplayName);
        var tenantId = principal.FindFirstValue(TenantId);

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(providerId) ||
            string.IsNullOrWhiteSpace(providerDisplayName) ||
            string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        return new AuthenticatedUser(userId, displayName, email, providerId, providerDisplayName, tenantId);
    }
}
