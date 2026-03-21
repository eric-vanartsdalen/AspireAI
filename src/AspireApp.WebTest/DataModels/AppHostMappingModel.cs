using Microsoft.Playwright;

namespace AspireApp.WebTest.DataModels;

public class AppHostMappingModel
{
	public IBrowserContext? BrowserContext { get; set; }
	public string AspireDashboardUri { get; set; } = string.Empty;
	public string AspireDashboardBrowserToken { get; set; } = string.Empty;
	public string AspireDashboardLoginUri
	{
		get
		{
			if (string.IsNullOrWhiteSpace(AspireDashboardUri) || string.IsNullOrWhiteSpace(AspireDashboardBrowserToken))
			{
				return string.Empty;
			}

			var dashboardBaseUri = AspireDashboardUri.EndsWith("/", StringComparison.Ordinal)
				? AspireDashboardUri
				: $"{AspireDashboardUri}/";

			var builder = new UriBuilder(new Uri(new Uri(dashboardBaseUri), "login"))
			{
				Query = $"t={Uri.EscapeDataString(AspireDashboardBrowserToken)}"
			};

			return builder.Uri.AbsoluteUri;
		}
	}
	public string WebfrontendUri { get; set; } = string.Empty;
	public string OllamaUri { get; set; } = string.Empty;

	public PageGotoOptions Options { get; } = new()
	{
		Timeout = 180000 // 3 minutes
	};
}
