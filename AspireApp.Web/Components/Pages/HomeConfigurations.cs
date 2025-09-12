namespace AspireApp.Web.Components.Pages;

public static class HomeConfigurations
{
	public static string ActiveModelController = "uninitialized";
	public static string ActiveModelURL = "uninitialized";
	public static string ActiveModel = "uninitialized";

	public static void PullConfigure()
	{
		// These are aspire connection and environment...
		// How is it best done to get the values across projects?
		ActiveModelController = Environment.GetEnvironmentVariable("AI-Controller-Type")
			?? "ollama";
		ActiveModelURL = ServiceDiscoveryUtilities.GetServiceConnectionString("ConnectionStrings__ollama")
			?? Environment.GetEnvironmentVariable("AI-Endpoint")
			?? "http://localhost:11434";
		ActiveModel = ServiceDiscoveryUtilities.GetServiceConnectionString("ConnectionStrings__chat")
			?? Environment.GetEnvironmentVariable("AI-Model")
			?? "phi4-mini:latest";

		// Aspire connection strings may have extra information
		if (ActiveModel.StartsWith(ActiveModelURL))
		{
			ActiveModel = ActiveModel.Replace(ActiveModelURL,"").Trim(';');
		}
		if (ActiveModel.StartsWith("Model="))
		{
			ActiveModel = ActiveModel.Replace("Model=", "");
		}
		if (ActiveModelURL.StartsWith("Endpoint="))
		{
			ActiveModelURL = ActiveModelURL.Replace("Endpoint=", "").Trim(';');
		}

		ServiceDiscoveryUtilities.ListAllServices();
		Console.WriteLine();
		Console.WriteLine("Active AI Type set to: " + ActiveModelController);
		Console.WriteLine("Active AI Endpoint set to: " + ActiveModelURL);
		Console.WriteLine("ACtive AI Model set to: " + ActiveModel);
	}
}
