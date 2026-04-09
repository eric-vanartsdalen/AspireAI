using AspireApp.Web.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;

namespace AspireApp.Web.Services;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddAspireAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ValidateLocalSeedConfiguration(configuration);

        var microsoftOptions = configuration.GetSection(MicrosoftEntraAuthenticationOptions.SectionName)
            .Get<MicrosoftEntraAuthenticationOptions>() ?? new MicrosoftEntraAuthenticationOptions();

        var authBuilder = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "aspireapp-auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.LoginPath = "/signin";
                options.LogoutPath = "/auth/signout";
            });

        // Only register the OIDC handler when the Microsoft config is actually present.
        // Registering it unconditionally would expose callback paths and attempt metadata
        // retrieval against an invalid authority.
        if (microsoftOptions.IsConfigured)
        {
            authBuilder.AddOpenIdConnect(MicrosoftEntraAuthService.AuthenticationScheme, options =>
            {
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.Authority = BuildMicrosoftAuthority(microsoftOptions.TenantId);
                options.ClientId = microsoftOptions.ClientId;
                options.ClientSecret = microsoftOptions.ClientSecret;
                options.CallbackPath = microsoftOptions.CallbackPath;
                options.SignedOutCallbackPath = microsoftOptions.SignedOutCallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = true;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.TokenValidationParameters.NameClaimType = "name";
                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var userDescriptor = BuildMicrosoftUserDescriptor(context.Principal);
                        if (userDescriptor is null)
                        {
                            context.Fail("The Microsoft Entra ID sign-in response did not include the claims required to establish the local app session.");
                            return;
                        }

                        var tenantManagement = context.HttpContext.RequestServices.GetRequiredService<TenantManagementService>();
                        var tenantSnapshot = await tenantManagement.EnsureTenantAccessAsync(
                            userDescriptor,
                            context.HttpContext.RequestAborted);

                        var appUser = new AuthenticatedUser(
                            userDescriptor.UserId,
                            userDescriptor.DisplayName,
                            userDescriptor.Email,
                            MicrosoftEntraAuthService.ProviderId,
                            "Microsoft",
                            tenantSnapshot.DefaultTenantId);

                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            RemoveClaimIfPresent(identity, ClaimTypes.AuthenticationMethod);
                            RemoveClaimIfPresent(identity, AuthenticatedUserClaims.ProviderDisplayName);
                            RemoveClaimIfPresent(identity, AuthenticatedUserClaims.TenantId);
                            AuthenticatedUserClaims.AddClaims(identity, appUser);
                        }
                    }
                };
            });
        }

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName));
        services.AddOptions<LocalAuthenticationOptions>()
            .Bind(configuration.GetSection(LocalAuthenticationOptions.SectionName));
        services.AddOptions<MicrosoftEntraAuthenticationOptions>()
            .Bind(configuration.GetSection(MicrosoftEntraAuthenticationOptions.SectionName));

        services.AddScoped<AuthenticationContext>();
        services.AddScoped<AppAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
        services.AddScoped<AuthServiceFactory>();
        services.AddScoped<IPasswordHasher<LocalAuthUser>, PasswordHasher<LocalAuthUser>>();
        services.AddScoped<TenantManagementService>();
        services.AddScoped<TenantStoreBootstrapper>();
        services.AddAuthServiceRegistration<LocalAuthService>(AuthenticationOptions.LocalService);
        services.AddAuthServiceRegistration<MicrosoftEntraAuthService>(AuthenticationOptions.MicrosoftService);
        services.AddAuthServiceRegistration<MockAuthService>(AuthenticationOptions.MockService);
        services.AddAuthServiceRegistration<CompositeAuthService>(AuthenticationOptions.CombinedService);
        services.AddScoped<LocalAccountAuthenticator>();
        services.AddScoped<LocalAuthBootstrapper>();
        services.AddScoped<IAuthService>(sp => sp.GetRequiredService<AuthServiceFactory>().Create());

        return services;
    }

    public static IServiceCollection AddAuthServiceRegistration<TService>(this IServiceCollection services, string serviceKey)
        where TService : class, IAuthService
    {
        if (string.IsNullOrWhiteSpace(serviceKey))
        {
            throw new ArgumentException("Authentication service key cannot be empty.", nameof(serviceKey));
        }

        services.AddScoped<TService>();
        services.AddSingleton(AuthServiceRegistration.Create<TService>(serviceKey.Trim()));

        return services;
    }

    private static string BuildMicrosoftAuthority(string? tenantId)
    {
        var effectiveTenantId = string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId.Trim();
        return $"https://login.microsoftonline.com/{effectiveTenantId}/v2.0";
    }

    private static void ValidateLocalSeedConfiguration(IConfiguration configuration)
    {
        var seedUsers = configuration.GetSection(LocalAuthenticationOptions.SectionName).GetSection("SeedUsers").GetChildren();
        foreach (var seedUser in seedUsers)
        {
            if (!string.IsNullOrWhiteSpace(seedUser["Password"]) ||
                !string.IsNullOrWhiteSpace(seedUser["PlaintextPassword"]))
            {
                throw new InvalidOperationException(
                    "Authentication:Local:SeedUsers only accepts precomputed PasswordHash values. Remove plaintext password settings.");
            }
        }
    }

    private static TenantUserDescriptor? BuildMicrosoftUserDescriptor(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email) ??
                    principal.FindFirstValue("preferred_username") ??
                    principal.FindFirstValue("upn");
        var displayName = principal.FindFirstValue("name") ?? principal.Identity?.Name ?? email;
        var userId = principal.FindFirstValue("oid") ??
                     principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                     email;

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return new TenantUserDescriptor(
            userId,
            displayName,
            email);
    }

    private static void RemoveClaimIfPresent(ClaimsIdentity identity, string claimType)
    {
        var claim = identity.FindFirst(claimType);
        if (claim is not null)
        {
            identity.RemoveClaim(claim);
        }
    }
}
