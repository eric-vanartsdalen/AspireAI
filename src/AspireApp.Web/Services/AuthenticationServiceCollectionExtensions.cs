using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

namespace AspireApp.Web.Services;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddAspireAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "aspireapp-auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = "/signin";
                options.LogoutPath = "/auth/mock/signout";
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.SectionName));

        services.AddScoped<AuthenticationContext>();
        services.AddScoped<AppAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
        services.AddScoped<AuthServiceFactory>();
        services.AddAuthServiceRegistration<MockAuthService>(AuthenticationOptions.DefaultService);
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
}
