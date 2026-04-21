extern alias web;

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticatedUserClaims = web::AspireApp.Web.Services.AuthenticatedUserClaims;
using AuthenticationContext = web::AspireApp.Web.Services.AuthenticationContext;
using MainLayout = web::AspireApp.Web.Components.Layout.MainLayout;
using Tenant = web::AspireApp.Web.Data.Tenant;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using TenantMembership = web::AspireApp.Web.Data.TenantMembership;
using TenantContextService = web::AspireApp.Web.Services.TenantContextService;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

public sealed class MainLayoutTests : IDisposable
{
    private readonly BunitContext _testContext = new();
    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public void ClosesSidebar_WhenLocationChanges()
    {
        RegisterAuthenticatedLayoutServices();

        var cut = _testContext.Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<MainLayout>(0);
                childBuilder.AddAttribute(1, "Body", (RenderFragment)(bodyBuilder => bodyBuilder.AddContent(0, "Body")));
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.Find(".sidebar-toggle").Click();
        Assert.Contains("sidebar-open", cut.Find(".page").ClassList);

        var navigationManager = _testContext.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/chat");

        cut.WaitForAssertion(() =>
            Assert.DoesNotContain("sidebar-open", cut.Find(".page").ClassList));
    }

    [Fact]
    public void ClosesSidebar_WhenTenantChanges()
    {
        var tenantContext = RegisterAuthenticatedLayoutServices(includeSecondaryTenant: true);

        var cut = _testContext.Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<MainLayout>(0);
                childBuilder.AddAttribute(1, "Body", (RenderFragment)(bodyBuilder => bodyBuilder.AddContent(0, "Body")));
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.Find(".sidebar-toggle").Click();
        Assert.Contains("sidebar-open", cut.Find(".page").ClassList);

        tenantContext.CurrentTenantId = "tenant-beta";

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("sidebar-open", cut.Find(".page").ClassList);
            Assert.Equal("tenant-beta", cut.Find("[data-auth-tenant]").GetAttribute("data-auth-tenant"));
        });
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _testContext.Dispose();
    }

    private TenantContextService RegisterAuthenticatedLayoutServices(bool includeSecondaryTenant = false)
    {
        var authenticationContext = new AuthenticationContext();
        var currentUser = new AuthenticatedUser(
            "demo-taylor-jones",
            "Taylor Jones",
            "taylor@example.com",
            "demo",
            "Demo provider",
            "tenant-alpha");

        authenticationContext.SetCurrentUser(currentUser);

        var tenantContext = includeSecondaryTenant
            ? CreateInitializedTenantContext(authenticationContext, currentUser)
            : new TenantContextService(authenticationContext);

        _testContext.Services.AddAuthorization();
        _testContext.Services.AddSingleton<IAuthorizationPolicyProvider>(new StubAuthorizationPolicyProvider());
        _testContext.Services.AddSingleton<IAuthorizationService>(new StubAuthorizationService());
        _testContext.Services.AddSingleton<AuthenticationStateProvider>(new StubAuthenticationStateProvider(currentUser));
        _testContext.Services.AddSingleton(authenticationContext);
        _testContext.Services.AddSingleton(tenantContext);
        return tenantContext;
    }

    private TenantContextService CreateInitializedTenantContext(
        AuthenticationContext authenticationContext,
        AuthenticatedUser currentUser)
    {
        var dbContext = CreateDbContext();
        _disposables.Add(dbContext);
        SeedOwnedTenant(dbContext, "tenant-alpha", currentUser.UserId, isProtected: true);
        SeedOwnedTenant(dbContext, "tenant-beta", currentUser.UserId);
        dbContext.SaveChanges();

        var tenantManagementService = new TenantManagementService(
            dbContext,
            NullLogger<TenantManagementService>.Instance);

        var tenantContext = new TenantContextService(tenantManagementService, authenticationContext);
        tenantContext.EnsureInitializedAsync().GetAwaiter().GetResult();
        return tenantContext;
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private static void SeedOwnedTenant(
        UploadDbContext context,
        string tenantId,
        string ownerUserId,
        bool isProtected = false)
    {
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = tenantId,
            OwnerUserId = ownerUserId,
            IsProtected = isProtected
        });

        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = ownerUserId,
            IsDefault = isProtected
        });
    }

    private sealed class StubAuthenticationStateProvider(AuthenticatedUser user) : AuthenticationStateProvider
    {
        private readonly AuthenticatedUser _user = user;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(authenticationType: "Test");
            AuthenticatedUserClaims.AddClaims(identity, _user);
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class StubAuthorizationPolicyProvider : IAuthorizationPolicyProvider
    {
        private static readonly AuthorizationPolicy AuthenticatedPolicy =
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            Task.FromResult(AuthenticatedPolicy);

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
            Task.FromResult<AuthorizationPolicy?>(AuthenticatedPolicy);

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            Task.FromResult<AuthorizationPolicy?>(AuthenticatedPolicy);
    }

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(
                user.Identity?.IsAuthenticated == true
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(
                user.Identity?.IsAuthenticated == true
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());
    }
}
