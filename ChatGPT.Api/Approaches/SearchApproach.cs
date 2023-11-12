using Azure.AI.OpenAI;
using Azure.Search.Documents.Models;
using ChatGPT.Api.Models;
using System.Runtime.CompilerServices;
using System.Text;
using Azure.Search.Documents;

namespace ChatGPT.Api.Approaches;

internal class SearchApproach : ApproachBase
{
    private readonly OpenAIClient _openAiClient;
    private readonly SearchClient _searchClient;

    private const string QueryMessage = """
                                        Di seguito è riportata la cronologia della conversazione effettuata fino a quel momento e una nuova domanda posta dall'utente a cui è necessario rispondere effettuando una ricerca in una sorgente del manuale e dei video di supporto.
                                        Hai accesso all'indice di ricerca cognitiva di Azure con centinaia di documenti.
                                        Genera una query di ricerca basata sulla conversazione e sulla nuova domanda.
                                        Non includere nomi di file di origine citati e nomi di documenti, ad esempio info.txt o doc.pdf, nei termini della query di ricerca.
                                        Non includere testo all'interno di [] o <<>> nei termini della query di ricerca.
                                        Non includere caratteri speciali come "+".
                                        Se la domanda non è in inglese, traducila in italiano prima di generare la query di ricerca.
                                        Se non riesci a generare una query di ricerca oppure se non è stata posta una domanda, restituisci solo il numero 0.
                                        """;

    private const string AdditionalSystemMessage = """
                                                   Ogni fonte è sempre nel formato <source uri="fonte" title="titolo">contenuto</source>. Utilizza le parentesi quadre per fare riferimento alla fonte, ad esempio [titolo::fonte]. Non combinare le fonti, elenca ciascuna fonte separatamente, ad esempio [titolo1::fonte1][titolo2::fonte2].
                                                   """;

    private static ChatMessage[] Samples = {
        new(ChatRole.User, "Come si configura Alexa?"),
        new(ChatRole.Assistant, "configurazione Alexa"),
        new(ChatRole.User, "Come si cambiano le batterie?"),
        new(ChatRole.Assistant, "sostituzione batterie")
    };

    public SearchApproach(OpenAIClient openAiClient, SearchClient searchClient)
    {
        _openAiClient = openAiClient;
        _searchClient = searchClient;
    }

    public override async IAsyncEnumerable<ChatAppResponse> ChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        var options = GetChatCompletionsOptions(request);
        options.Temperature = 0;

        // Sostituisco il system message predefinito
        options.Messages.RemoveAt(0);
        options.Messages.Insert(0, new(ChatRole.System, QueryMessage));

        // Aggiungo i messaggi di esempio
        foreach (var sample in Samples.Reverse())
        {
            options.Messages.Insert(1, sample);
        }

        // Ottengo la query di ricerca
        var queryResponse = await _openAiClient.GetChatCompletionsAsync(GptModel, options, token);
        if (queryResponse is null)
        {
            yield return new ChatAppResponseOrError
            {
                Error = "No response"
            };
            yield break;
        }

        var queryText = queryResponse.Value.Choices[0].Message.Content;
        // Se la query è 0, uso l'ultima domanda
        if (queryText == "0")
        {
           queryText = request.Messages[^1].Content;
        }

        var embeddingResponse = await _openAiClient.GetEmbeddingsAsync(AdaModel, new(queryText), token);
        if (embeddingResponse is null)
        {
            yield return new ChatAppResponseOrError
            {
                Error = "No response"
            };
            yield break;
        }

        var queryVectors = embeddingResponse.Value.Data[0].Embedding.ToArray();
        var searchResponse = await _searchClient.SearchAsync<SearchDocument>(queryText,new()
        {
            Select = { "title","text","uri" },
            Filter = "public eq true",
            Size = 3,
            Vectors =
            {
                new ()
                {
                    Fields = { "vector" },
                    KNearestNeighborsCount = 50,
                    Value = queryVectors
                }
            }
        }, token);

        // Preparo le fonti
        var source = new StringBuilder();
        await foreach (var result in searchResponse.Value.GetResultsAsync())
        {
            var uri = result.Document["uri"].ToString();
            var text = result.Document["text"].ToString();
            var title = result.Document["title"].ToString();
            source.AppendLine($"""<source uri="{uri}" title="{title}">{text}</source>""");
        }

        options = GetChatCompletionsOptions(request);
        // Aggiorno istruzioni per gestire le fonti
        options.Messages[0].Content += AdditionalSystemMessage;
        // Aggiungo le fonti
        options.Messages[^1].Content += $"\n\nFonti:\n{source}";

        // Ottengo la risposta
        var response = await _openAiClient.GetChatCompletionsStreamingAsync(GptModel, options, token);
        token.ThrowIfCancellationRequested();

        await foreach (var chatAppResponse in ProcessResponseAsync(response, token))
        {
            token.ThrowIfCancellationRequested();
            yield return chatAppResponse;
        }
    }
}
