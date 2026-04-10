using Microsoft.SemanticKernel.ChatCompletion;
using AspireApp.Web.Services;

namespace AspireApp.Web.Components.Shared;

public static class ChatHistoryService
{
    public static IEnumerable<(string User, string Message)> GetFormattedMessages(this Microsoft.SemanticKernel.ChatCompletion.ChatHistory history)
    {
        if (history.Count > 0)
        {
            foreach (var message in history.ToList())
            {
                yield return (
                    User: message.Role == AuthorRole.User ? "User" : "Assistant", // Replace AuthorName with Role
                    Message: message.Content ?? string.Empty
                );
            }
        }
    }

    public static ChatHistory ToChatHistory(this IEnumerable<ChatConversationMessageRecord> messages)
    {
        var history = new ChatHistory();

        foreach (var message in messages.OrderBy(message => message.Sequence))
        {
            if (string.Equals(message.Role, ChatConversationRoles.User, StringComparison.OrdinalIgnoreCase))
            {
                history.AddUserMessage(message.Content);
                continue;
            }

            history.AddAssistantMessage(message.Content);
        }

        return history;
    }
}

