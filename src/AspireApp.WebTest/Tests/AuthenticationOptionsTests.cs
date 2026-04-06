extern alias web;

using AuthenticationOptions = web::AspireApp.Web.Services.AuthenticationOptions;

namespace AspireApp.WebTest.Tests;

public class AuthenticationOptionsTests
{
    [Theory]
    [InlineData(AuthenticationOptions.AutoService, false, false, AuthenticationOptions.MockService)]
    [InlineData(AuthenticationOptions.AutoService, true, false, AuthenticationOptions.CombinedService)]
    [InlineData(AuthenticationOptions.AutoService, false, true, AuthenticationOptions.CombinedService)]
    [InlineData(AuthenticationOptions.AutoService, true, true, AuthenticationOptions.CombinedService)]
    [InlineData(AuthenticationOptions.LocalService, false, false, AuthenticationOptions.LocalService)]
    [InlineData(AuthenticationOptions.LocalService, true, true, AuthenticationOptions.LocalService)]
    [InlineData(AuthenticationOptions.MicrosoftService, true, false, AuthenticationOptions.MicrosoftService)]
    [InlineData(AuthenticationOptions.MockService, false, true, AuthenticationOptions.MockService)]
    [InlineData(AuthenticationOptions.CombinedService, true, true, AuthenticationOptions.CombinedService)]
    public void ResolveEffectiveService_ReturnsExpectedService(
        string configuredService,
        bool microsoftConfigured,
        bool localConfigured,
        string expectedService)
    {
        var resolvedService = AuthenticationOptions.ResolveEffectiveService(
            configuredService,
            microsoftConfigured,
            localConfigured);

        Assert.Equal(expectedService, resolvedService);
    }
}
