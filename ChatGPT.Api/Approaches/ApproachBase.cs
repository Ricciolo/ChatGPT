using Azure.AI.OpenAI;
using ChatGPT.Api.Models;

using System;
using System.Runtime.CompilerServices;

namespace ChatGPT.Api.Approaches;

internal abstract class ApproachBase
{
    protected const string SystemMessage = """
                                         Sei un assitente di nome "Hello Sure" sviluppato dalla Easydom (sede italiana in Via Monte Santo, 14 - 20, 20851 Lissone MB. Telefono 0287168663).
                                         Il tuo colore preferito è l'azzurro del logo.
                                         Rispondi solo a domande relative al sistema di antifurto di nome "Hello Sure".
                                         Se non conosci la risposta in base alla sorgente rimanda l'utente al sito https://www.hellosure.app.
                                         Non inventare mai risposte o rispondere con frasi non presenti nelle sorgenti.
                                         Rispondi sempre nella lingua in cui è stata fatta la domanda e cerca di essere breve nelle risposte.
                                         """;

    protected const string GptModel = "gpt-4";
    protected const string AdaModel = "ada";

    public abstract IAsyncEnumerable<ChatAppResponse> ChatAsync(ChatRequest request, CancellationToken token);

    protected virtual ChatCompletionsOptions GetChatCompletionsOptions(ChatRequest request)
    {
        var options = new ChatCompletionsOptions
        {
            ChoiceCount = 1,
            MaxTokens = 1024,
            Temperature = 1f,
            Messages =
            {
                new (ChatRole.System, SystemMessage)
            }
        };
        foreach (var requestMessage in request.Messages)
        {
            options.Messages.Add(new(requestMessage.Role, requestMessage.Content));
        }

        return options;
    }

    protected async IAsyncEnumerable<ChatAppResponse> ProcessResponseAsync(Azure.Response<StreamingChatCompletions>? response, [EnumeratorCancellation] CancellationToken token)
    {
        if (response is null)
        {
            yield return new ChatAppResponseOrError
            {
                Error = "No response"
            };
            yield break;
        }

        await foreach (var choice in response.Value.GetChoicesStreaming(token))
        {
            await foreach (var message in choice.GetMessageStreaming(token))
            {
                if (message is null) continue;

                yield return new ChatAppResponse
                {
                    Choices =
                    {
                        new()
                        {
                            Index = choice.Index.GetValueOrDefault(),
                            Message = new()
                            {
                                Content = message.Content,
                                Role = message.Role.ToString()!
                            }
                        }
                    }
                };
            }
        }
    }
}
