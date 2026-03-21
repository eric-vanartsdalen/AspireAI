using Microsoft.Playwright;

namespace AspireApp.WebTest.DataModels;

public class AppHostMappingModel
{
	public IPage? Page { get; set; }
	public string WebfrontendUri { get; set; } = string.Empty;
	public string OllamaUri { get; set; } = string.Empty;

	public PageGotoOptions Options = new PageGotoOptions()
	{
		Timeout = 180000 // 3 minutes
	};

}
