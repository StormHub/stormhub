using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Agents.WebApi;

internal sealed class OllamaChatClient(OllamaApiClient ollamaApiClient) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await ((IChatClient)ollamaApiClient).GetResponseAsync(messages, options, cancellationToken);
        foreach (var chatMessage in response.Messages)
        {
            chatMessage.MessageId ??= Guid.NewGuid().ToString();
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in ((IChatClient)ollamaApiClient)
                       .GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            // https://github.com/ag-ui-protocol/ag-ui requires MessageId to be set on updates
            // but OllamaSharp only sets ResponseId on updates.
            update.MessageId ??= update.ResponseId;
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => 
        ((IChatClient)ollamaApiClient).GetService(serviceType, serviceKey);

    public void Dispose() => ollamaApiClient.Dispose();
}