using Microsoft.SemanticKernel.ChatCompletion;

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
}

