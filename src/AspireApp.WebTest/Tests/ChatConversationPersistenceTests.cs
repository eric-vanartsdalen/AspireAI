using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.Playwright;
using Npgsql;
using System.Text.RegularExpressions;
using Xunit.v3.Priority;

namespace AspireApp.WebTest.Tests;

[TestCaseOrderer(typeof(PriorityOrderer))]
public sealed class ChatConversationPersistenceTests : IClassFixture<TestFixture>
{
    private const string MockProviderId = "demo";
    private const string OwnerUserId = "demo-taylor-jones";
    private const string OtherUserId = "demo-robin-singh";

    private static readonly Regex SendButtonRegex = new("send query|send|processing", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlaceholderConversationTitleRegex = new(
        "^(new (conversation|chat)|untitled|conversation)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppHostMappingModel _mapping;
    private readonly IBrowser _browser;

    public ChatConversationPersistenceTests(TestFixture fixture)
    {
        _mapping = fixture.AppHostMapping;
        Assert.NotNull(_mapping.Browser);
        _browser = _mapping.Browser!;
    }

    [Fact, Priority(2)]
    public async Task SignedInUserCanSaveRenameResumeAndDeleteConversation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var firstPrompt = $"Buster persistence smoke {suffix}: explain why chat history must stay private per user.";
        var secondPrompt = $"Buster second thread {suffix}: this conversation must stay separate.";
        var followUpPrompt = $"Buster follow up {suffix}: continue the first saved conversation only.";
        var renamedTitle = $"Buster rename {suffix}";

        await WithPageAsync(async page =>
        {
            await SignInToChatPageAsync(page, OwnerUserId);
            await EnsureConversationUxAvailableOrSkipAsync(page);

            await SendPromptAsync(page, firstPrompt);

            var generatedTitle = await WaitForGeneratedConversationTitleAsync(page);
            Assert.False(string.IsNullOrWhiteSpace(generatedTitle));
            Assert.DoesNotMatch(PlaceholderConversationTitleRegex, generatedTitle);

            await RenameCurrentConversationAsync(page, renamedTitle, verifyFocusWhileTyping: true);
            await WaitForConversationItemVisibleAsync(page, renamedTitle);

            await ClickNewConversationAsync(page);
            await SendPromptAsync(page, secondPrompt);

            await SelectConversationAsync(page, renamedTitle);
            var transcript = await WaitForTranscriptToContainAsync(page, firstPrompt);
            Assert.Contains(firstPrompt, transcript, StringComparison.Ordinal);
            Assert.DoesNotContain(secondPrompt, transcript, StringComparison.Ordinal);

            await SendPromptAsync(page, followUpPrompt);
            transcript = await WaitForTranscriptToContainAsync(page, followUpPrompt);
            Assert.Contains(followUpPrompt, transcript, StringComparison.Ordinal);

            await DeleteCurrentConversationAsync(page);
            await WaitForConversationItemAbsentAsync(page, renamedTitle);
        });
    }

    [Fact, Priority(2)]
    public async Task ConversationsRemainPrivateEvenWithinSharedTenantMembership()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sharedTenantId = $"tenant-chat-shared-{suffix}";
        var sharedTenantName = $"Shared chat tenant {suffix}";
        var privateTitle = $"Buster private {suffix}";
        var privatePrompt = $"Buster private chat {suffix}: this must stay hidden from other users.";
        var privateConversationUrl = string.Empty;

        await EnsureSharedTenantMembershipAsync(sharedTenantId, sharedTenantName);

        try
        {
            await WithPageAsync(async ownerPage =>
            {
                await SignInToChatPageAsync(ownerPage, OwnerUserId);
                await EnsureConversationUxAvailableOrSkipAsync(ownerPage);
                await SelectTenantAsync(ownerPage, sharedTenantId);

                await SendPromptAsync(ownerPage, privatePrompt);
                await RenameCurrentConversationAsync(ownerPage, privateTitle);
                await WaitForConversationItemVisibleAsync(ownerPage, privateTitle);
                privateConversationUrl = ownerPage.Url;

                await WithPageAsync(async otherPage =>
                {
                    await SignInToChatPageAsync(otherPage, OtherUserId);
                    await EnsureConversationUxAvailableOrSkipAsync(otherPage);
                    await SelectTenantAsync(otherPage, sharedTenantId);

                    await WaitForConversationItemAbsentAsync(otherPage, privateTitle);

                    if (!string.IsNullOrWhiteSpace(privateConversationUrl) &&
                        !string.Equals(privateConversationUrl, BuildAbsoluteUri(_mapping.WebfrontendUri, "chat"), StringComparison.OrdinalIgnoreCase))
                    {
                        await otherPage.GotoAsync(privateConversationUrl, _mapping.Options);
                        await WaitForPageLoadCompletion(otherPage);
                    }

                    var transcript = await TryReadVisibleTranscriptAsync(otherPage);
                    Assert.DoesNotContain(privatePrompt, transcript, StringComparison.Ordinal);
                    Assert.Null(await TryFindConversationItemByTitleAsync(otherPage, privateTitle));
                });

                await DeleteCurrentConversationAsync(ownerPage);
                await WaitForConversationItemAbsentAsync(ownerPage, privateTitle);
            });
        }
        finally
        {
            await DeleteTenantAsync(sharedTenantId);
        }
    }

    private async Task SignInToChatPageAsync(IPage page, string userId)
    {
        var signInPath =
            $"auth/mock/signin?providerId={Uri.EscapeDataString(MockProviderId)}&userId={Uri.EscapeDataString(userId)}&returnUrl=%2Fchat";
        await page.GotoAsync(BuildAbsoluteUri(_mapping.WebfrontendUri, signInPath), _mapping.Options);
        await WaitForPageLoadCompletion(page);

        var timeoutAt = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < timeoutAt)
        {
            var chatSurface = await FindVisibleAsync(page,
                current => current.Locator("[data-testid='chat-conversations-shell']"),
                current => current.Locator("#chat-messages-container"),
                current => current.GetByRole(AriaRole.Heading, new() { Name = "AI Chatbot" }));

            if (chatSurface is not null)
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Signed-in navigation never reached the chat surface.");
    }

    private static async Task EnsureConversationUxAvailableOrSkipAsync(IPage page)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeoutAt)
        {
            var shell = await FindVisibleAsync(page,
                current => current.Locator("[data-testid='chat-conversations-shell']"),
                current => current.Locator("[data-testid='chat-conversation-list']"),
                current => current.Locator("[data-testid='chat-new-conversation']"));

            if (shell is not null)
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.SkipWhen(
            true,
            "Chat persistence UX is not available in this checkout yet. Jeff needs to land the saved-conversation shell plus stable hooks for chat-conversations-shell, chat-conversation-list, chat-new-conversation, chat-conversation-item, chat-current-conversation-title, chat-conversation-rename, chat-conversation-title-input, and chat-conversation-delete.");
    }

    private static async Task SendPromptAsync(IPage page, string prompt)
    {
        var input = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='chat-message-input']"),
            current => current.Locator("input[placeholder*='question']"),
            current => current.Locator("div.input-group input.form-control"));
        Assert.NotNull(input);

        await input!.FillAsync(prompt);

        var sendButton = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='chat-send']"),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = SendButtonRegex }));
        Assert.NotNull(sendButton);

        await WaitForControlEnabledAsync(sendButton!, "chat send button");
        await sendButton!.ClickAsync();
        await WaitForTranscriptToContainAsync(page, prompt);
        await WaitForControlEnabledAsync(sendButton, "chat send button");
    }

    private static async Task<string> WaitForGeneratedConversationTitleAsync(IPage page)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(45);
        var currentTitle = string.Empty;

        while (DateTime.UtcNow < timeoutAt)
        {
            currentTitle = await TryReadCurrentConversationTitleAsync(page);
            if (!string.IsNullOrWhiteSpace(currentTitle) &&
                !PlaceholderConversationTitleRegex.IsMatch(currentTitle))
            {
                return currentTitle;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Conversation title never resolved from a generated summary. Last title: '{currentTitle}'.");
        return string.Empty;
    }

    private static async Task RenameCurrentConversationAsync(
        IPage page,
        string renamedTitle,
        bool verifyFocusWhileTyping = false)
    {
        var renameButton = await RequireVisibleAsync(page,
            "[data-testid='chat-conversation-rename']",
            "conversation rename button");
        await renameButton.ClickAsync();

        var titleInput = await RequireVisibleAsync(page,
            "[data-testid='chat-conversation-title-input']",
            "conversation title input");

        if (!verifyFocusWhileTyping)
        {
            await titleInput.FillAsync(renamedTitle);
        }
        else
        {
            await titleInput.FillAsync(string.Empty);
            await titleInput.ClickAsync();

            var chatInput = await RequireVisibleAsync(page,
                "[data-testid='chat-message-input']",
                "chat message input");
            var chatInputValueBeforeTyping = await chatInput.InputValueAsync();

            var firstCharacter = renamedTitle[..1];
            await titleInput.PressSequentiallyAsync(firstCharacter);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            await AssertActiveElementTestIdAsync(page, "chat-conversation-title-input", "rename title input after typing the first character");
            Assert.Equal(firstCharacter, await titleInput.InputValueAsync());
            Assert.Equal(chatInputValueBeforeTyping, await chatInput.InputValueAsync());

            if (renamedTitle.Length > 1)
            {
                await titleInput.PressSequentiallyAsync(renamedTitle[1..]);
                await Task.Delay(250, TestContext.Current.CancellationToken);
            }

            await AssertActiveElementTestIdAsync(page, "chat-conversation-title-input", "rename title input after typing the full title");
            Assert.Equal(renamedTitle, await titleInput.InputValueAsync());
            Assert.Equal(chatInputValueBeforeTyping, await chatInput.InputValueAsync());
        }

        var saveButton = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='chat-conversation-save-title']"),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("save|done|rename", RegexOptions.IgnoreCase) }));

        if (saveButton is not null)
        {
            await saveButton.ClickAsync();
        }
        else
        {
            await titleInput.PressAsync("Enter");
        }

        var currentTitle = await WaitForCurrentConversationTitleAsync(page, renamedTitle);
        Assert.Equal(renamedTitle, currentTitle);
    }

    private static async Task ClickNewConversationAsync(IPage page)
    {
        var previousTitle = await TryReadCurrentConversationTitleAsync(page);
        var newConversationButton = await RequireVisibleAsync(
            page,
            "[data-testid='chat-new-conversation']",
            "new conversation button");

        await newConversationButton.ClickAsync();

        var timeoutAt = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeoutAt)
        {
            var currentTitle = await TryReadCurrentConversationTitleAsync(page);
            if (string.IsNullOrWhiteSpace(currentTitle) ||
                !string.Equals(previousTitle, currentTitle, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Creating a new conversation did not move away from the previously selected conversation '{previousTitle}'.");
    }

    private static async Task SelectConversationAsync(IPage page, string title)
    {
        var item = await RequireConversationItemByTitleAsync(page, title);
        await item.ClickAsync();
        await WaitForCurrentConversationTitleAsync(page, title);
    }

    private static async Task DeleteCurrentConversationAsync(IPage page)
    {
        await page.EvaluateAsync("() => { window.confirm = () => true; }");

        var deleteButton = await RequireVisibleAsync(page,
            "[data-testid='chat-conversation-delete']",
            "conversation delete button");
        await deleteButton.ClickAsync();

        var confirmButton = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='chat-confirm-delete']"));

        if (confirmButton is not null)
        {
            await confirmButton.ClickAsync();
        }
    }

    private static async Task<string> WaitForTranscriptToContainAsync(IPage page, string expectedText)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(90);
        var transcript = string.Empty;

        while (DateTime.UtcNow < timeoutAt)
        {
            transcript = await TryReadVisibleTranscriptAsync(page);
            if (transcript.Contains(expectedText, StringComparison.Ordinal))
            {
                return transcript;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Visible transcript never contained '{expectedText}'. Final transcript: {transcript}");
        return string.Empty;
    }

    private static async Task<string> TryReadVisibleTranscriptAsync(IPage page)
    {
        var transcriptSurface = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='chat-thread']"),
            current => current.Locator("#chat-messages-container"),
            current => current.Locator("#chathistory"));

        return transcriptSurface is null
            ? string.Empty
            : (await transcriptSurface.TextContentAsync()) ?? string.Empty;
    }

    private static async Task<string> TryReadCurrentConversationTitleAsync(IPage page)
    {
        var currentTitle = page.Locator("[data-testid='chat-current-conversation-title']").First;
        if (!await currentTitle.IsVisibleAsync())
        {
            return string.Empty;
        }

        return (await currentTitle.TextContentAsync())?.Trim() ?? string.Empty;
    }

    private static async Task<string> WaitForCurrentConversationTitleAsync(IPage page, string expectedTitle)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < timeoutAt)
        {
            var currentTitle = await TryReadCurrentConversationTitleAsync(page);
            if (string.Equals(currentTitle, expectedTitle, StringComparison.Ordinal))
            {
                return currentTitle;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Current conversation title never became '{expectedTitle}'.");
        return string.Empty;
    }

    private static async Task WaitForConversationItemVisibleAsync(IPage page, string title)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < timeoutAt)
        {
            var item = await TryFindConversationItemByTitleAsync(page, title);
            if (item is not null && await item.IsVisibleAsync())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Conversation '{title}' never appeared in the saved conversation list.");
    }

    private static async Task WaitForConversationItemAbsentAsync(IPage page, string title)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < timeoutAt)
        {
            var item = await TryFindConversationItemByTitleAsync(page, title);
            if (item is null || !await item.IsVisibleAsync())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Conversation '{title}' remained visible when it should have been absent.");
    }

    private static async Task AssertActiveElementTestIdAsync(IPage page, string expectedTestId, string description)
    {
        var activeElementTestId = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-testid') ?? ''");

        Assert.True(
            string.Equals(expectedTestId, activeElementTestId, StringComparison.Ordinal),
            $"{description} expected active element '{expectedTestId}' but found '{activeElementTestId}'.");
    }

    private static async Task<ILocator> RequireConversationItemByTitleAsync(IPage page, string title)
    {
        var item = await TryFindConversationItemByTitleAsync(page, title);
        Assert.NotNull(item);
        return item!;
    }

    private static async Task<ILocator?> TryFindConversationItemByTitleAsync(IPage page, string title)
    {
        var items = await page.Locator("[data-testid='chat-conversation-item']").AllAsync();
        foreach (var item in items)
        {
            var text = (await item.TextContentAsync())?.Trim();
            if (!string.IsNullOrWhiteSpace(text) &&
                text.Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static async Task<ILocator> RequireVisibleAsync(IPage page, string selector, string description)
    {
        var locator = page.Locator(selector).First;
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000
            });
        }
        catch (PlaywrightException ex)
        {
            Assert.Fail($"Could not find the {description}. Selector: {selector}. Error: {ex.Message}");
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Could not find the {description}. Selector: {selector}.");
        }

        return locator;
    }

    private static async Task WaitForControlEnabledAsync(ILocator locator, string description)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (!await locator.IsDisabledAsync())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The {description} stayed disabled longer than expected.");
    }

    private async Task SelectTenantAsync(IPage page, string tenantId)
    {
        var tenantSelector = page.Locator("#tenant-select").First;
        await tenantSelector.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });

        var availableTenantIds = await tenantSelector.EvaluateAsync<string[]>(
            "select => Array.from(select.options).map(option => option.value).filter(Boolean)");

        Assert.Contains(tenantId, availableTenantIds);

        await tenantSelector.SelectOptionAsync(new SelectOptionValue { Value = tenantId });

        var selectedTenantId = await tenantSelector.InputValueAsync();
        Assert.Equal(tenantId, selectedTenantId);
    }

    private async Task EnsureSharedTenantMembershipAsync(string tenantId, string tenantName)
    {
        await using var connection = new NpgsqlConnection(_mapping.UploadStoreConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await using (var tenantCommand = new NpgsqlCommand("""
            INSERT INTO tenants (id, name, owner_user_id, is_protected, created_at, updated_at)
            VALUES (@id, @name, @ownerUserId, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (id) DO NOTHING
            """, connection, transaction))
        {
            tenantCommand.Parameters.AddWithValue("id", tenantId);
            tenantCommand.Parameters.AddWithValue("name", tenantName);
            tenantCommand.Parameters.AddWithValue("ownerUserId", OwnerUserId);
            await tenantCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await InsertTenantMembershipAsync(connection, transaction, tenantId, OwnerUserId);
        await InsertTenantMembershipAsync(connection, transaction, tenantId, OtherUserId);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private static async Task InsertTenantMembershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        string userId)
    {
        await using var membershipCommand = new NpgsqlCommand("""
            INSERT INTO tenant_memberships (tenant_id, user_id, is_default, created_at)
            VALUES (@tenantId, @userId, FALSE, CURRENT_TIMESTAMP)
            ON CONFLICT (tenant_id, user_id) DO NOTHING
            """, connection, transaction);
        membershipCommand.Parameters.AddWithValue("tenantId", tenantId);
        membershipCommand.Parameters.AddWithValue("userId", userId);
        await membershipCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task DeleteTenantAsync(string tenantId)
    {
        await using var connection = new NpgsqlConnection(_mapping.UploadStoreConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("DELETE FROM tenants WHERE id = @tenantId", connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task WithPageAsync(Func<IPage, Task> testAction)
    {
        await using var browserContext = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });

        var page = await browserContext.NewPageAsync();

        try
        {
            await testAction(page);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task<ILocator?> FindVisibleAsync(IPage page, params Func<IPage, ILocator>[] candidates)
    {
        foreach (var candidateFactory in candidates)
        {
            var candidate = candidateFactory(page).First;
            try
            {
                if (await candidate.IsVisibleAsync())
                {
                    return candidate;
                }
            }
            catch (PlaywrightException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        return null;
    }

    private static string BuildAbsoluteUri(string baseUri, string relativePath)
    {
        return new Uri(new Uri($"{baseUri.TrimEnd('/')}/"), relativePath).AbsoluteUri;
    }

    private static async Task WaitForPageLoadCompletion(IPage page)
    {
        await Task.WhenAll(
            page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 10_000 }),
            page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10_000 }));
    }
}
