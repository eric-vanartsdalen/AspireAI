using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Playwright;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;
using Xunit.v3.Priority;

namespace AspireApp.WebTest.Tests;

[TestCaseOrderer(typeof(PriorityOrderer))]
public class BasicAspireAppHostTests : IClassFixture<TestFixture>
{
    private const string AspireTableNameColumn = "table tbody tr td[col-index='1']";
    private const string AspireTableResourceColumn = "table tbody tr td[col-index='4']";
    private const string AspireTableLinksColumn = "table tbody tr td[col-index='5']";
    private const string FileNameCell = "table[class='file-table'] tbody tr td[class='file-name-cell']";
    private const string FullFilenameCell = "span[class='file-name-full']";
    private const string FileTableRow = "table[class='file-table'] tbody tr";
    private const string FilenameCellInRow = "td[class='file-name-cell'] span[class='file-name-full']";
    private const string DeleteButtonInCell = "td button";
    private static readonly string TestFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "AspireApp.WebTest", "DataExample", "increase_green_energy_one_rooftop_at_a_time.pdf");
    private readonly AppHostMappingModel _data;
    protected IBrowserContext _browserContext;

    public BasicAspireAppHostTests(TestFixture fixture)
    {
        _data = fixture.AppHostMapping;
        Assert.NotNull(_data.BrowserContext);
        _browserContext = _data.BrowserContext!;
    }

    [Fact, Priority(1)]
    public async Task AspireDashboardLoads()
    {
        Assert.False(string.IsNullOrWhiteSpace(_data.AspireDashboardLoginUri));

        IPage page = await _browserContext.NewPageAsync();

        // Navigate to the tokenized login URI. The dashboard validates the
        // browser token, sets an auth cookie, and redirects to the root page.
        await page.GotoAsync(_data.AspireDashboardLoginUri, _data.Options);
        await WaitForPageLoadCompletion(page);

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

        await WaitForPageLoadCompletion(page);
        var title = await page.TitleAsync();
        if (!title.Equals("AspireApp resources", StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"Page title is not as expected: {title}");
            // wait for 2 seconds and check the title again to see if it was just delayed in updating.
            await Task.Delay(2000);
            title = await page.TitleAsync();
        }

        // Check basic Dashboard items
        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Equivalent("AspireApp resources", title);

        // Capture all the Configured AppHost Resources and where appropropriate, their expected configuration endpoint links.
        await page.Locator(AspireTableNameColumn).First.WaitForAsync(new LocatorWaitForOptions()
        {
            Timeout = 5000
        });
        List<ILocator> rowNames = (await page.Locator(AspireTableNameColumn).AllAsync()).ToList();
        List<ILocator> rowResources = (await page.Locator(AspireTableResourceColumn).AllAsync()).ToList();
        List<ILocator> rowLinks = (await page.Locator(AspireTableLinksColumn).AllAsync()).ToList();
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

    [Fact, Priority(1)]
    public async Task WebHomeUILoads()
    {
        Console.WriteLine($"Navigating to Web Frontend at: {_data.WebfrontendUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.WebfrontendUri, _data.Options);
        await WaitForPageLoadCompletion(page);
        // Check page title equal "Home"
        var title = await page.TitleAsync();
        Assert.Equivalent("Home", title);
    }

    [Fact, Priority(1)]
    public async Task OllamaLoads()
    {
        Console.WriteLine($"Navigating to Ollama Frontend at: {_data.OllamaUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.OllamaUri, _data.Options);
        await WaitForPageLoadCompletion(page);
        // Check the page contains text "Ollama is running"
        var content = await page.ContentAsync();
        Assert.Contains("Ollama is running", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact, Priority(1)]
    public async Task PythonServiceLoads()
    {
        Console.WriteLine($"Navigating to Python Services Frontend at: {_data.PythonServiceUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.PythonServiceUri, _data.Options);
        await WaitForPageLoadCompletion(page);
        // Check the page shows json of python service
        var content = await page.ContentAsync();
        Assert.Contains("AspireAI Document Processing Service", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact, Priority(1)]
    public async Task PythonServiceOpenAPILoads()
    {
        var PythonOpenAPIEndpoint = $"{_data.PythonServiceUri.TrimEnd('/')}/docs";
        Console.WriteLine($"Navigating to Python Services Open API at: {PythonOpenAPIEndpoint}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(PythonOpenAPIEndpoint, _data.Options);
        await WaitForPageLoadCompletion(page);
        // Check the page shows json of python service
        var title = await page.TitleAsync();
        var content = await page.ContentAsync();
        Assert.Equivalent("AspireAI Document Processing Service - Swagger UI", title);
        Assert.Contains("AspireAI Document Processing Service", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact, Priority(1)]
    public async Task GraphDbLoads()
    {
        Console.WriteLine($"Navigating to Graph DB Services Frontend at: {_data.GraphDBUri}");
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.GraphDBUri, _data.Options);
        await WaitForPageLoadCompletion(page);
        // Check the Graph DB page shows expected content
        var title = await page.TitleAsync();
        Assert.Equivalent("Neo4j Browser", title);
    }

    [Fact, Priority(0)]
    public async Task FlowEndToEnd()
    {
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.WebfrontendUri, _data.Options);
        await WaitForPageLoadCompletion(page);
        // This test is currently a placeholder as it requires setting up test data and may involve
        // more complex interactions with the UI and backend services.
        //
        // Using an available PDF to test the end-to-end flow of:
        // - uploading a document example (file is in AspireApp.Webtest DataExample directory - increase_green_energy_ one_rooftop_at_a_time.pdf)
        // (https://github.com/Azure-Samples/azure-search-sample-data/blob/main/ai-enrichment-mixed-media/increase_green_energy_%20one_rooftop_at_a_time.pdf),
        // STEP: Click the Upload Documents tab in the Web Frontend, upload the document, and submit it for processing.

        await ClickByRole(AriaRole.Link, "Upload Documents", page);

        await SetUploadInput(TestFile, AriaRole.Button, "Choose File", page);

        await ClickByRole(AriaRole.Button, "Start Upload", page);

        IReadOnlyList<ILocator> filenameCells = await page.Locator(FileNameCell).AllAsync();
        string filename = PullFilename(TestFile).Split(".")[0];

        foreach (var fileCell in filenameCells)
        {
            var cellText = await fileCell.Locator(FullFilenameCell).TextContentAsync();
            if (cellText.StartsWith(filename.Split(".")[0]))
            {
                Debug.WriteLine($"Found file ({cellText}) that starts with {filename}");
            }
            Debug.WriteLine(cellText);
        }
        // TO_DO: After uploading the document, the test should ideally verify the subsequent steps in the processing pipeline, which may require more complex setup and interactions:
        // - processing it with the Python service
        // STEP: Verify the Python service received the document, processed it, and sent the expected results to the Graph DB.

        // - verifying the results in the Graph DB
        // STEP: Navigate to the Graph DB frontend, login, run a query to verify the processed data from the uploaded document is present and correct.

        // - (eventually use Chat AI to query the ingested document in the Web Frontend.//)
        // STEP: Navigate to the AI Chat tab in the Web Frontend, ask a question related to the content of the uploaded document, and verify the response is accurate based on the document's content.

    }

    [Fact, Priority(2)]
    public async Task DeleteUploadedTestFile()
    {
        IPage page = await _browserContext.NewPageAsync();
        await page.GotoAsync(_data.WebfrontendUri, _data.Options);
        await WaitForPageLoadCompletion(page);
        // STEP: Navigate to Upload Documents tab in the Web Frontend, identify the test document in the list of uploaded documents, and click the delete button to remove it from the system.
        await ClickByRole(AriaRole.Link, "Upload Documents", page);

        IReadOnlyList<ILocator> filenameRows = await GetDocumentSourceTableRows(page);
        ILocator targetFileToDelete = null;
        string filename = PullFilename(TestFile).Split(".")[0];

        targetFileToDelete = await PullFileDeleteButton(filenameRows, filename);
        if (targetFileToDelete != null)
        {
            await targetFileToDelete.HoverAsync();
            await targetFileToDelete.ClickAsync(new LocatorClickOptions() { Delay = 250 });
            await WaitForPageLoadCompletion(page);

            // STEP: Verify the file is deleted from the list of uploaded documents in the Web Frontend.
            IReadOnlyList<ILocator> updatedFilenameRows = await GetDocumentSourceTableRows(page);
            int countDown = 5;
            while (updatedFilenameRows.Count() >= filenameRows.Count() && countDown > 0)
            {
                await Task.Delay(1000);
                countDown--;
                updatedFilenameRows = await GetDocumentSourceTableRows(page);
                await WaitForPageLoadCompletion(page);
            }
            if (updatedFilenameRows.Count() >= filenameRows.Count())
            {
                Debug.WriteLine("Problem - File deletion did not complete within the expected time.");
            }
            if (updatedFilenameRows.Count > 0)
            {
                foreach (var fileCell in updatedFilenameRows)
                {
                    var cellText = await fileCell.Locator(FullFilenameCell).TextContentAsync();
                    Assert.False(cellText.StartsWith(filename.Split(".")[0]), $"File {cellText} should have been deleted but still appears in the list.");
                    Debug.WriteLine(cellText);
                }
            }
        }
        else
        {
            Debug.WriteLine($"Could not find file that starts with {filename} to delete. It may have already been deleted or never uploaded.");
        }
    }

    private static async Task<IReadOnlyList<ILocator>> GetDocumentSourceTableRows(IPage page)
    {
        try
        {
            await WaitForLocator(page.Locator(FileTableRow));
            IReadOnlyList<ILocator> updatedFilenameCells = await page.Locator(FileTableRow).AllAsync();
            return updatedFilenameCells;
        }
        catch (Exception e)
        {
            // return empty list as it appears to be empty
            return Array.Empty<ILocator>();
        }
    }

    private static async Task<ILocator> PullFileDeleteButton(IReadOnlyList<ILocator> filenameRows, string filename)
    {
        ILocator targetFileToDelete = null;
        int countDown = 10;
        await CheckAtleastOneCellExists(filenameRows);
        while (targetFileToDelete == null && countDown > 0)
        {
            countDown--;
            // Look for the file in the list of uploaded files and get its corresponding delete button locator.
            foreach (var fileRow in filenameRows)
            {
                var cellText = await fileRow.Locator(FilenameCellInRow).TextContentAsync();
                if (cellText.StartsWith(filename.Split(".")[0]))
                {
                    IReadOnlyList<ILocator> deleteButtons = await fileRow.Locator(DeleteButtonInCell).AllAsync();
                    targetFileToDelete = deleteButtons.FirstOrDefault();
                    Debug.WriteLine($"Found file ({cellText}) that starts with {filename}");
                    return targetFileToDelete;
                }
                Debug.WriteLine(cellText);
            }
        }
        return null;
    }

    private static async Task CheckAtleastOneCellExists(IReadOnlyList<ILocator> filenameRows)
    {
        if (filenameRows.Count() == 0)
        {
            Debug.WriteLine("No file rows found as list is empty");
            throw new InvalidOperationException("No file rows found in the table. It may be that there are no files uploaded or there is an issue with the locator.");
        }
        var cells = await filenameRows[0].Locator(FilenameCellInRow).AllAsync();
        if (cells.Count() == 0)
        {
            // wait and try again as it may be that the file list is still loading.
            await Task.Delay(2000);
            cells = await filenameRows[0].Locator(FilenameCellInRow).AllAsync();
        }
        if (cells.Count() == 0)
        {
            Debug.WriteLine("No file name spans found within the first cell");
            throw new InvalidOperationException("No file name spans found within the first cell. It may be that the table structure has changed or there is an issue with the locator.");
        }
    }

    private static async Task SetUploadInput(string testDocumentPath, AriaRole role, string nameText, IPage page)
    {
        ILocator control = page.GetByRole(role, new() { Name = nameText });
        await WaitForLocator(control);
        var inputValue = PullFilename(await control.InputValueAsync());
        int countDown = 5;
        while (inputValue != PullFilename(testDocumentPath) && countDown > 0)
        {
            countDown--;
            var fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
            {
                await control.HoverAsync();
                await control.ClickAsync();
                await WaitForPageLoadCompletion(page);
            });
            await fileChooser.SetFilesAsync(testDocumentPath, new FileChooserSetFilesOptions { Timeout = 5000 });
            await WaitForPageLoadCompletion(page);
            inputValue = PullFilename(await control.InputValueAsync());
        }
        if (inputValue != PullFilename(testDocumentPath))
        {
            throw new InvalidAsynchronousStateException($"File input value '{inputValue}' does not match expected path '{testDocumentPath}'.");
        }
    }

    private static string PullFilename(string filePathName)
    {
        // pull only the filename from the full path.
        if (string.IsNullOrWhiteSpace(filePathName))
        {
            return string.Empty;
        }
        // Handle both Windows and Unix-style paths
        char[] separators = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return filePathName.Split(separators, StringSplitOptions.RemoveEmptyEntries).Last();
    }

    private static async Task ClickByRole(AriaRole role, string name, IPage page)
    {
        ILocator element = page.GetByRole(role, new() { Name = name });
        await WaitForLocator(element);
        await element.HoverAsync();
        await element.ClickAsync(new LocatorClickOptions() { Delay = 250 });
        await WaitForPageLoadCompletion(page);
    }

    private static async Task WaitForLocator(ILocator locator, int timeout = 5000)
    {
        await locator.First.WaitForAsync(new LocatorWaitForOptions()
        {
            Timeout = timeout,
            State = WaitForSelectorState.Visible
        });
    }

    private static async Task WaitForPageLoadCompletion(IPage page)
    {
        // Wait for the page to reach a stable state after navigation or interaction
        await Task.WhenAll(
            page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 10000 }),
            page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 }),
            page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10000 }),
            page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 10000 })
        );
    }
}
