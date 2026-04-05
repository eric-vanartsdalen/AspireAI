extern alias web;

using AuthenticationOptions = web::AspireApp.Web.Services.AuthenticationOptions;

namespace AspireApp.WebTest.Tests;

public class AuthenticationOptionsTests
{
    [Theory]
    [InlineData(AuthenticationOptions.AutoService, true, AuthenticationOptions.CombinedService)]
    [InlineData(AuthenticationOptions.AutoService, false, AuthenticationOptions.MockService)]
    [InlineData(AuthenticationOptions.MicrosoftService, true, AuthenticationOptions.MicrosoftService)]
    [InlineData(AuthenticationOptions.MockService, true, AuthenticationOptions.MockService)]
    [InlineData(AuthenticationOptions.CombinedService, true, AuthenticationOptions.CombinedService)]
    public void ResolveEffectiveService_ReturnsExpectedService(string configuredService, bool microsoftConfigured, string expectedService)
    {
        var resolvedService = AuthenticationOptions.ResolveEffectiveService(configuredService, microsoftConfigured);

        Assert.Equal(expectedService, resolvedService);
    }
}
