using System.Collections;

namespace AspireApp.Web;

public static class ServiceDiscoveryUtilities
{
	public static List<string> GetServices()
	{
		List<string> ret = new List<string>();
		// get environmentvariables and sort
		var envVars = Environment.GetEnvironmentVariables()
			.Cast<DictionaryEntry>()
			.OrderBy(e => e.Key.ToString())
			.Select(e => $"{e.Key}={e.Value}");
		foreach (var variable in envVars)
		{
			ret.Add(variable.ToString());
		}
		return ret;
	}

	public static string GetServiceConnectionString(string serviceName)
	{
		try
		{
			// Fix for CS8603: Return an empty string if the environment variable is null
			return Environment.GetEnvironmentVariable($"ConnectionStrings__{serviceName}") ?? string.Empty;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error retrieving connection string for service '{serviceName}': {ex.Message}");
			return string.Empty;
		}
	}

	public static string? GetServiceEndpoint(string serviceName, string endpointName, int index = 0) =>
		Environment.GetEnvironmentVariable($"services__{serviceName}__{endpointName}__{index}");
}
