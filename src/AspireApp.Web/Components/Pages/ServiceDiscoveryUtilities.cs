using System.Collections;

namespace AspireApp.Web.Components.Pages;

public static class ServiceDiscoveryUtilities
{
    public static string GetServiceConnectionString(string serviceName)
    {
        // Check service endpoint format used by Aspire
        var endpointValue = Environment.GetEnvironmentVariable($"{serviceName}");
        if (!string.IsNullOrEmpty(endpointValue))
        {
            return endpointValue;
        }
        Console.WriteLine($"Service {serviceName} not found");
        return string.Empty;
    }

    public static List<string> ListAllServices()
    {
        List<string> environmentVariables = new List<string>();
        
        // Get all environment variables and sort them by key name
        var sortedVars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .OrderBy(e => e.Key.ToString())
            .ToList();
        
        Console.WriteLine("===== Available Environment Variables (Sorted) =====");
        foreach (var env in sortedVars)
        {
            var entry = $"{env.Key} = {env.Value}";
            environmentVariables.Add(entry);
            Console.WriteLine(entry);
        }
        Console.WriteLine("=================================================");
        
        return environmentVariables;
    }
}