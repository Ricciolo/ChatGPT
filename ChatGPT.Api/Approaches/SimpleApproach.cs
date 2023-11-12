using Azure.AI.OpenAI;
using ChatGPT.Api.Models;

using System;
using System.Runtime.CompilerServices;

namespace ChatGPT.Api.Approaches;

internal class SimpleApproach : ApproachBase
{
    private readonly OpenAIClient _openAiClient;

    public SimpleApproach(OpenAIClient openAiClient)
    {
        _openAiClient = openAiClient;
    }

    public override async IAsyncEnumerable<ChatAppResponse> ChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        var options = GetChatCompletionsOptions(request);

        var response = await _openAiClient.GetChatCompletionsStreamingAsync(GptModel, options, token);
        token.ThrowIfCancellationRequested();

        await foreach (var chatAppResponse in ProcessResponseAsync(response, token))
        {
            token.ThrowIfCancellationRequested();
            yield return chatAppResponse;
        }
    }
}
