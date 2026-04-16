using Microsoft.Playwright;

namespace AspireApp.WebTest.DataModels;

public class AppHostMappingModel
{
	public IBrowser? Browser { get; set; }

	public string AspireDashboardLoginUri { get; set; } = string.Empty;
	public string WebfrontendUri { get; set; } = string.Empty;
	public string OllamaUri { get; set; } = string.Empty;
    public string GraphDBUri { get; set; } = string.Empty;
    public string LightRagUri { get; set; } = string.Empty;
    public string PythonServiceUri { get; set; } = string.Empty;
    public string BrainGatewayUri { get; set; } = string.Empty;
    public string SharedDataPath { get; set; } = string.Empty;
    public string SharedDatabasePath { get; set; } = string.Empty;
    public string UploadStoreConnectionString { get; set; } = string.Empty;

    public PageGotoOptions Options { get; } = new()
	{
		Timeout = 180000 // 3 minutes
	};
}
