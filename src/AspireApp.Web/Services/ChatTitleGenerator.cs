using AspireApp.Web.Components.Pages;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AspireApp.Web.Services;

public interface IChatTitleGenerator
{
    Task<string?> TryGenerateTitleAsync(IReadOnlyList<ChatConversationMessageRecord> messages, CancellationToken cancellationToken = default);
}

public sealed class ChatTitleGenerator(ILogger<ChatTitleGenerator> logger) : IChatTitleGenerator
{
    private readonly ILogger<ChatTitleGenerator> _logger = logger;

    public async Task<string?> TryGenerateTitleAsync(
        IReadOnlyList<ChatConversationMessageRecord> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        HomeConfigurations.PullConfigure();

        if (!Uri.TryCreate(HomeConfigurations.ActiveModelURL, UriKind.Absolute, out var endpoint) ||
            string.IsNullOrWhiteSpace(HomeConfigurations.ActiveModel))
        {
            return null;
        }

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            IKernelBuilder builder = Kernel.CreateBuilder();
            builder.AddOllamaChatCompletion(
                modelId: HomeConfigurations.ActiveModel,
                endpoint: endpoint);

            var kernel = builder.Build();
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage(
                """
                Create a concise title for this conversation.
                Rules:
                - 2 to 6 words
                - sentence case
                - no quotation marks
                - no markdown
                - no trailing punctuation
                - focus on the user's intent
                """);
            history.AddUserMessage(BuildExcerpt(messages));

            var response = await chatCompletionService.GetChatMessageContentAsync(
                history,
                cancellationToken: timeoutTokenSource.Token);

            return response.Content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Chat title generation timed out; keeping fallback title.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Chat title generation failed; keeping fallback title.");
            return null;
        }
    }

    private static string BuildExcerpt(IReadOnlyList<ChatConversationMessageRecord> messages)
    {
        var excerptMessages = messages
            .Take(4)
            .Select(message =>
            {
                var speaker = string.Equals(message.Role, ChatConversationRoles.User, StringComparison.OrdinalIgnoreCase)
                    ? "User"
                    : "Assistant";

                var content = message.Content.Length <= 500
                    ? message.Content
                    : $"{message.Content[..499]}…";

                return $"{speaker}: {content}";
            });

        return string.Join(Environment.NewLine, excerptMessages);
    }
}
