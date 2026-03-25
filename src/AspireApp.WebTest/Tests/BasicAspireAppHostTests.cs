using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Playwright;
using System.Diagnostics;

namespace AspireApp.WebTest.Tests;

public class BasicAspireAppHostTests : IClassFixture<TestFixture>
{
    private readonly AppHostMappingModel _data;
    protected IBrowserContext _browserContext;

    public BasicAspireAppHostTests(TestFixture fixture)
    {
        _data = fixture.AppHostMapping;
        Assert.NotNull(_data.BrowserContext);
        _browserContext = _data.BrowserContext!;
    }

    [Fact]
    public async Task AspireDashboardLoads()
    {
        Assert.False(string.IsNullOrWhiteSpace(_data.AspireDashboardLoginUri));

        IPage page = await _browserContext.NewPageAsync();

        // Navigate to the tokenized login URI. The dashboard validates the
        // browser token, sets an auth cookie, and redirects to the root page.
        await page.GotoAsync(_data.AspireDashboardLoginUri, _data.Options);

        // Wait for the post-authentication redirect to leave /login.
        // If GotoAsync already followed a server-side 302 this resolves immediately;
        // otherwise it waits for the Blazor-driven redirect after token validation.
        await page.WaitForURLAsync(
            url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = 120_000 });

        // Blazor Server sets the page title asynchronously via <PageTitle>
        // after the SignalR circuit initializes. Give it generous time.
        await page.WaitForFunctionAsync(
            "() => document.title && document.title.length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });

        var title = await page.TitleAsync();

        // Check basic Dashboard items
        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Equivalent("AspireApp resources", title);

        // Capture all the Configured AppHost Resources and where appropropriate, their expected configuration endpoint links.
        await page.Locator("table tbody tr td[col-index='1']").First.WaitForAsync(new LocatorWaitForOptions()
        {
            Timeout = 5000
        });
        List<ILocator> rowNames = (await page.Locator("table tbody tr td[col-index='1']").AllAsync()).ToList();
        List<ILocator> rowResources = (await page.Locator("table tbody tr td[col-index='4']").AllAsync()).ToList();
        List<ILocator> rowLinks = (await page.Locator("table tbody tr td[col-index='5']").AllAsync()).ToList();
        string ollamaLink = string.Empty;
        string webFrontendLink = string.Empty;
        string graphDbLink = string.Empty;
        string lightRagLink = string.Empty;
        string pythonServiceLink = string.Empty;
        string apiServiceLink = string.Empty;

        for (int i = 0; i < rowNames.Count; i++)
        {
            var name = await rowNames[i].TextContentAsync();
            var resource = await rowResources[i].TextContentAsync();
            var link = await rowLinks[i].TextContentAsync();
            var message = $"{name.Trim()} | {resource.Trim()} | {link.Trim()}";
            Console.WriteLine(message);
            if (name.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                ollamaLink = link.Trim();
            }
            if (name.Contains("WebFrontend", StringComparison.OrdinalIgnoreCase))
            {
                webFrontendLink = link.Trim();
            }
            if (name.Contains("Graph-Db", StringComparison.OrdinalIgnoreCase))
            {
                graphDbLink = link.Trim();
            }
            if (name.Contains("LightRag", StringComparison.OrdinalIgnoreCase))
            {
                lightRagLink = link.Trim();
            }
            if (name.Contains("Python-Service", StringComparison.OrdinalIgnoreCase))
            {
                pythonServiceLink = link.Trim();
            }
            if (name.Contains("ApiService", StringComparison.OrdinalIgnoreCase))
            {
                apiServiceLink = link.Trim();
            }
        }
        // Find service for Ollama and Web Frontend and check their links are correct.
        Assert.False(string.IsNullOrWhiteSpace(ollamaLink), "Ollama link should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(webFrontendLink), "Web Frontend link should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(graphDbLink), "Graph-Db link should not be empty");
        Assert.False(string.IsNullOrEmpty(pythonServiceLink), "PythonService link should not be empty");
        Assert.False(string.IsNullOrEmpty(apiServiceLink), "ApiService link should not be empty");

        Assert.True(ollamaLink.Contains(_data.OllamaUri, StringComparison.OrdinalIgnoreCase),
            $"Ollama link ({ollamaLink}) should contain {_data.OllamaUri}");
        Assert.True(webFrontendLink.Contains(_data.WebfrontendUri, StringComparison.OrdinalIgnoreCase),
            $"Web Frontend link ({webFrontendLink}) should contain {_data.WebfrontendUri}");
        Assert.True(graphDbLink.Contains(_data.GraphDBUri, StringComparison.OrdinalIgnoreCase),
            $"Graph-Db link ({graphDbLink}) should contain {_data.GraphDBUri}");
        if (!string.IsNullOrWhiteSpace(lightRagLink))
        {
            Assert.True(lightRagLink.Contains(_data.LightRagUri, StringComparison.OrdinalIgnoreCase),
                $"LightRag link ({lightRagLink}) should contain {_data.LightRagUri}");
        }
        Assert.True(pythonServiceLink.Contains(_data.PythonServiceUri, StringComparison.OrdinalIgnoreCase),
            $"PythonService link ({pythonServiceLink}) should contain {_data.PythonServiceUri}");
        Assert.True(apiServiceLink.Contains(_data.ApiServiceUri, StringComparison.OrdinalIgnoreCase),
            $"ApiService link ({apiServiceLink}) should contain {_data.ApiServiceUri}");
    }

    [Fact]
    public async Task WebHomeUILoads()
    {
        Console.WriteLine($"Navigating to Web Frontend at: {_data.WebfrontendUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.WebfrontendUri, _data.Options);
        // Check page title equal "Home"
        var title = await page.TitleAsync();
        Assert.Equivalent("Home", title);
    }

    [Fact]
    public async Task OllamaLoads()
    {
        Console.WriteLine($"Navigating to Ollama Frontend at: {_data.OllamaUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.OllamaUri, _data.Options);
        // Check the page contains text "Ollama is running"
        var content = await page.ContentAsync();
        Assert.Contains("Ollama is running", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PythonServiceLoads()
    {
        Console.WriteLine($"Navigating to Python Services Frontend at: {_data.PythonServiceUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.PythonServiceUri, _data.Options);
        // Check the page shows json of python service
        var content = await page.ContentAsync();
        Assert.Contains("AspireAI Document Processing Service", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PythonServiceOpenAPILoads()
    {
        var PythonOpenAPIEndpoint = $"{_data.PythonServiceUri.TrimEnd('/')}/docs";
        Console.WriteLine($"Navigating to Python Services Open API at: {PythonOpenAPIEndpoint}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(PythonOpenAPIEndpoint, _data.Options);
        // Check the page shows json of python service
        var title = await page.TitleAsync();
        var content = await page.ContentAsync();
        Assert.Equivalent("AspireAI Document Processing Service - Swagger UI", title);
        Assert.Contains("AspireAI Document Processing Service", content, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task GraphDbLoads()
    {
        Console.WriteLine($"Navigating to Graph DB Services Frontend at: {_data.GraphDBUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.GraphDBUri, _data.Options);
        // Check the Graph DB page shows expected content
        var title = await page.TitleAsync();
        Assert.Equivalent("Neo4j Browser", title);
    }

    [Fact]
    public async Task DeleteUploadedTestFile()
    {
        // ARRANGE: Get the test document locations
        var testDocumentPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "AspireApp.WebTest", "DataExample", "increase_green_energy_one_rooftop_at_a_time.pdf");

        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.WebfrontendUri, _data.Options);
        // STEP: Navigate to Upload Documents tab in the Web Frontend, identify the test document in the list of uploaded documents, and click the delete button to remove it from the system.
        ILocator UploadDocumentsTab = page.GetByRole(AriaRole.Link, new() { Name = "Upload Documents" }); // page.Locator("a[href='upload']");
        await UploadDocumentsTab.HoverAsync();
        await UploadDocumentsTab.ClickAsync();

        IReadOnlyList<ILocator> filenameCells = await page.Locator("table[class='file-table'] tbody tr td[class='file-name-cell']").AllAsync();
        foreach (var fileCell in filenameCells)
        {
            var cellText = await fileCell.Locator("span").TextContentAsync();
            Console.Write(cellText);
        }

    }

    [Fact]
    public async Task FlowEndToEnd()
    {
        // ARRANGE: Get the test document locations
        var testDocumentPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "AspireApp.WebTest", "DataExample", "increase_green_energy_one_rooftop_at_a_time.pdf");

        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.WebfrontendUri, _data.Options);
        // This test is currently a placeholder as it requires setting up test data and may involve
        // more complex interactions with the UI and backend services.
        //
        // Using an available PDF to test the end-to-end flow of:
        // - uploading a document example (file is in AspireApp.Webtest DataExample directory - increase_green_energy_ one_rooftop_at_a_time.pdf)
        // (https://github.com/Azure-Samples/azure-search-sample-data/blob/main/ai-enrichment-mixed-media/increase_green_energy_%20one_rooftop_at_a_time.pdf),
        // STEP: Click the Upload Documents tab in the Web Frontend, upload the document, and submit it for processing.

        // await page.GetByRole(AriaRole.Link, new() { Name = "Upload Documents" }).ClickAsync();
        ILocator UploadDocumentsTab = page.GetByRole(AriaRole.Link, new() { Name = "Upload Documents" }); // page.Locator("a[href='upload']");
        await UploadDocumentsTab.HoverAsync();
        await UploadDocumentsTab.ClickAsync();

        ILocator ChooseFileButton = page.GetByRole(AriaRole.Button, new() { Name = "Choose File" });
        var fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
        {
            await ChooseFileButton.HoverAsync();
            await ChooseFileButton.ClickAsync();
        });
        await fileChooser.SetFilesAsync(testDocumentPath);

        ILocator StartUploadButton = page.GetByRole(AriaRole.Button, new() { Name = "Start Upload" });
        await StartUploadButton.HoverAsync();
        await StartUploadButton.ClickAsync();

        IReadOnlyList<ILocator> filenameCells = await page.Locator("table[class='file-table'] tbody tr td[class='file-name-cell']").AllAsync();
        foreach(var fileCell in filenameCells)
        {
            var cellText = await fileCell.Locator("span").TextContentAsync();
            Console.Write(cellText);
        }
        // - processing it with the Python service
        // STEP: Verify the Python service received the document, processed it, and sent the expected results to the Graph DB.

        // - verifying the results in the Graph DB
        // STEP: Navigate to the Graph DB frontend, login, run a query to verify the processed data from the uploaded document is present and correct.

        // - (eventually use Chat AI to query the ingested document in the Web Frontend.//)
        // STEP: Navigate to the AI Chat tab in the Web Frontend, ask a question related to the content of the uploaded document, and verify the response is accurate based on the document's content.

    }
}
