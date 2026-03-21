using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.Playwright;

namespace AspireApp.WebTest.Tests;

public class BasicAspireAppHostTests : IClassFixture<TestFixture>
{
	private readonly AppHostMappingModel _data;
	protected IPage page;

	public BasicAspireAppHostTests(TestFixture fixture)
	{
		_data = fixture.AppHostMapping;
		Assert.NotNull(_data.Page);
		page = _data.Page;
	}

	[Fact]
	public async Task WebLoads()
	{
		Console.WriteLine($"Navigating to Web Frontend at: {_data.WebfrontendUri}");
		await page.GotoAsync(_data.WebfrontendUri, _data.Options);
		// Check page title equal "Home"
		var title = await page.TitleAsync();
		Assert.Contains("Home", title, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task OllamaLoads()
	{
		Console.WriteLine($"Navigating to Ollama Frontend at: {_data.OllamaUri}");
		await page.GotoAsync(_data.OllamaUri, _data.Options);
		// Check the page contains text "Ollama is running"
		var content = await page.ContentAsync();
		Assert.Contains("Ollama is running", content, StringComparison.OrdinalIgnoreCase);
	}
}
