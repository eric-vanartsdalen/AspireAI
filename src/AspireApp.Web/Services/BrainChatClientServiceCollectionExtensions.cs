using Microsoft.Extensions.Http.Resilience;

namespace AspireApp.Web.Services;

public static class BrainChatClientServiceCollectionExtensions
{
    public static IHttpClientBuilder AddBrainGatewayChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddHttpClient<IBrainChatClient, BrainChatClient>((_, client) =>
        {
            var configuredBaseAddress = configuration["BRAIN_GATEWAY_URL"]
                ?? Environment.GetEnvironmentVariable("BRAIN_GATEWAY_URL");
            client.BaseAddress = new Uri(
                string.IsNullOrWhiteSpace(configuredBaseAddress)
                    ? "http://localhost:5158/"
                    : EnsureTrailingSlash(configuredBaseAddress));
            client.Timeout = TimeSpan.FromMinutes(3);
        });

#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers();
        builder.AddStandardResilienceHandler(options =>
        {
            // Chat requests are POSTs that may trigger real work downstream, so retrying them
            // amplifies deterministic failures and risks duplicate side effects.
            options.Retry.DisableForUnsafeHttpMethods();
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
        });
#pragma warning restore EXTEXP0001

        return builder;
    }

    private static string EnsureTrailingSlash(string baseAddress) =>
        baseAddress.EndsWith("/", StringComparison.Ordinal) ? baseAddress : $"{baseAddress}/";
}
