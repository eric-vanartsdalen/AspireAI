using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;

namespace AspireApp.Web.Services;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddAspireAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
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
                    OnTokenValidated = context =>
                    {
                        var appUser = BuildMicrosoftUser(context.Principal, microsoftOptions);
                        if (appUser is null)
                        {
                            context.Fail("The Microsoft Entra ID sign-in response did not include the claims required to establish the local app session.");
                            return Task.CompletedTask;
                        }

                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            RemoveClaimIfPresent(identity, ClaimTypes.AuthenticationMethod);
                            RemoveClaimIfPresent(identity, AuthenticatedUserClaims.ProviderDisplayName);
                            RemoveClaimIfPresent(identity, AuthenticatedUserClaims.TenantId);
                            AuthenticatedUserClaims.AddClaims(identity, appUser);
                        }

                        return Task.CompletedTask;
                    }
                };
            });
        }

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName));
        services.AddOptions<MicrosoftEntraAuthenticationOptions>()
            .Bind(configuration.GetSection(MicrosoftEntraAuthenticationOptions.SectionName));

        services.AddScoped<AuthenticationContext>();
        services.AddScoped<AppAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
        services.AddScoped<AuthServiceFactory>();
        services.AddAuthServiceRegistration<MockAuthService>(AuthenticationOptions.DefaultService);
        services.AddAuthServiceRegistration<MicrosoftEntraAuthService>(AuthenticationOptions.MicrosoftService);
        services.AddAuthServiceRegistration<CompositeAuthService>(AuthenticationOptions.CombinedService);
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

    private static AuthenticatedUser? BuildMicrosoftUser(ClaimsPrincipal? principal, MicrosoftEntraAuthenticationOptions options)
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

        return new AuthenticatedUser(
            userId,
            displayName,
            email,
            MicrosoftEntraAuthService.ProviderId,
            "Microsoft Entra ID",
            ResolveTenantSeed(email, options));
    }

    private static string ResolveTenantSeed(string email, MicrosoftEntraAuthenticationOptions options)
    {
        if (options.UserTenantSeeds.TryGetValue(email, out var userTenant) &&
            !string.IsNullOrWhiteSpace(userTenant))
        {
            return userTenant;
        }

        var atIndex = email.LastIndexOf('@');
        if (atIndex > -1)
        {
            var domain = email[(atIndex + 1)..];
            if (options.DomainTenantSeeds.TryGetValue(domain, out var domainTenant) &&
                !string.IsNullOrWhiteSpace(domainTenant))
            {
                return domainTenant;
            }
        }

        return string.IsNullOrWhiteSpace(options.DefaultAppTenantId)
            ? "default"
            : options.DefaultAppTenantId;
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
