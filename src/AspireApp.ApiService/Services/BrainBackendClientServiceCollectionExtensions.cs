using Microsoft.Extensions.Http.Resilience;

namespace AspireApp.ApiService.Services;

public static class BrainBackendClientServiceCollectionExtensions
{
    public static IHttpClientBuilder AddBrainBackendClient(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddHttpClient<IBrainBackendClient, PythonBrainBackendClient>((_, client) =>
        {
            var configuredBaseAddress = configuration["PYTHON_SERVICE_URL"];
            client.BaseAddress = new Uri(
                string.IsNullOrWhiteSpace(configuredBaseAddress)
                    ? "http://localhost:8000/"
                    : EnsureTrailingSlash(configuredBaseAddress));
            client.Timeout = TimeSpan.FromMinutes(3);
        });

#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers();
        builder.AddStandardResilienceHandler(options =>
        {
            // The gateway issues POST requests that may enqueue work or generate responses,
            // so retries on non-idempotent methods create noisy duplicate failures.
            options.Retry.DisableForUnsafeHttpMethods();
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(6);
        });
#pragma warning restore EXTEXP0001

        return builder;
    }

    private static string EnsureTrailingSlash(string baseAddress) =>
        baseAddress.EndsWith("/", StringComparison.Ordinal) ? baseAddress : $"{baseAddress}/";
}
