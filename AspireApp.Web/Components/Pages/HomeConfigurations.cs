namespace AspireApp.Web.Components.Pages;

public static class HomeConfigurations
{
	public static string AspireOllamaUri = "uninitialized";
	public static string AspireOllamaModel = "uninitialized";
	public static string EnvAIEndpoint = "uninitialized";
	public static string EnvAIModel = "uninitialized";

	public static void PullConfigure()
	{
		// These are aspire connection and environment...
		// How is it best done to get the values across projects?
		AspireOllamaUri = ServiceDiscoveryUtilities.GetServiceConnectionString("ConnectionStrings__ollama");
		AspireOllamaModel = ServiceDiscoveryUtilities.GetServiceConnectionString("ConnectionStrings__chat");
		EnvAIEndpoint = Environment.GetEnvironmentVariable("AI_Endpoint");
		EnvAIModel = Environment.GetEnvironmentVariable("AI_Model");

		ServiceDiscoveryUtilities.ListAllServices();
		Console.WriteLine();
		Console.WriteLine("AspireOllamaUri set to: " + AspireOllamaUri);
		Console.WriteLine("AspireOllamaModel set to: " + AspireOllamaModel); // Fixed typo here
		Console.WriteLine("EnvAIEndpoint set to: " + EnvAIEndpoint);
		Console.WriteLine("EnvAIModel set to: " + EnvAIModel);
	}
}
