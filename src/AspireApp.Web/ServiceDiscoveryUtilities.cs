using System.Collections;

namespace AspireApp.Web;

public static class ServiceDiscoveryUtilities
{
    public static List<string> GetServices() =>
        Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .OrderBy(entry => entry.Key?.ToString(), StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}={entry.Value}")
            .ToList();

    public static List<string> ListAllServices() => GetServices();

    public static string GetServiceConnectionString(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return string.Empty;
        }

        var candidates = serviceName.StartsWith("ConnectionStrings__", StringComparison.Ordinal)
            ? new[] { serviceName, serviceName["ConnectionStrings__".Length..] }
            : new[] { $"ConnectionStrings__{serviceName}", serviceName };

        foreach (var candidate in candidates)
        {
            var value = Environment.GetEnvironmentVariable(candidate);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    public static string? GetServiceEndpoint(string serviceName, string endpointName, int index = 0) =>
        Environment.GetEnvironmentVariable($"services__{serviceName}__{endpointName}__{index}");
}
