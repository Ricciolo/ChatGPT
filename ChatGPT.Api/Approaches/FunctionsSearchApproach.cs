using System.Globalization;
using Azure.AI.OpenAI;
using Azure.Search.Documents.Models;
using ChatGPT.Api.Models;
using System.Runtime.CompilerServices;
using System.Text;
using Azure.Search.Documents;
using System.Text.Json;
using Azure;

namespace ChatGPT.Api.Approaches;

internal class FunctionsSearchApproach : ApproachBase
{
    private readonly OpenAIClient _openAiClient;
    private readonly SearchClient _searchClient;
    private readonly ILogger<FunctionsSearchApproach> _logger;

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

    private const string QueryFunction = "query";
    private const string EventsFunction = "events";

    private static readonly ChatMessage[] Samples = {
        new(ChatRole.User, "Come si configura Alexa?"),
        new(ChatRole.Assistant, "configurazione Alexa"),
        new(ChatRole.User, "Come si cambiano le batterie?"),
        new(ChatRole.Assistant, "sostituzione batterie")
    };

    private static readonly string[] EventNames =
    {
        "Allarme inserito",
        "Allarme disinserito",
        "Allarme intrusione sulla porta soggiorno",
        "Manomissione sensore camera",
    };

    private static JsonSerializerOptions JsonSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FunctionsSearchApproach(OpenAIClient openAiClient, SearchClient searchClient, ILogger<FunctionsSearchApproach> logger)
    {
        _openAiClient = openAiClient;
        _searchClient = searchClient;
        _logger = logger;
    }

    protected override ChatCompletionsOptions GetChatCompletionsOptions(ChatRequest request)
    {
        var options = base.GetChatCompletionsOptions(request);
        options.Functions = GetFunctions();
        return options;
    }

    public override async IAsyncEnumerable<ChatAppResponse> ChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        var options = GetChatCompletionsOptions(request);

        var loop = true;
        var anyFunctionCalled = false;
        Response<StreamingChatCompletions>? response = null;

        while (loop)
        {
            // Ottengo la risposta
            var contentFilter = false;
            try
            {
                response = await _openAiClient.GetChatCompletionsStreamingAsync(GptModel, options, token);
                token.ThrowIfCancellationRequested();
            }
            catch (RequestFailedException ex) when (ex.ErrorCode=="content_filter")
            {
                contentFilter = true;
            }

            // Input considerato offensivo
            if (contentFilter)
            {
                yield return new ChatAppResponseOrError
                {
                    Error = "Content moderated"
                };
                yield break;
            }

            await foreach (var choice in response.Value.GetChoicesStreaming(token))
            {
                // Attendo la fine della generazione per poter valutare se ho finito o meno
                while ((choice.FinishReason.ToString()?.Length ?? 0) == 0)
                {
                    await Task.Delay(100, token);
                }

                if (choice.FinishReason == CompletionsFinishReason.TokenLimitReached)
                {
                    yield return new ChatAppResponseOrError
                    {
                        Error = "Token limit reached"
                    };
                    yield break;
                }

                _logger.LogInformation("Reason: {FinishReason}", choice.FinishReason);

                // E' richiesta una funzione
                if (choice.FinishReason == CompletionsFinishReason.FunctionCall)
                {
                    // Cerco la funzione chiamata
                    string? functionName = null;
                    string arguments = string.Empty;
                    await foreach (var m in choice.GetMessageStreaming(token))
                    {
                        functionName ??= m.FunctionCall?.Name;
                        arguments += m.FunctionCall?.Arguments;
                    }
                    if (functionName == null)
                    {
                        throw new InvalidOperationException("Function call is null");
                    }

                    anyFunctionCalled = true;

                    // Chiamo la funzione
                    await CallFunctionAsync(request, options, functionName, arguments, token);
                }
                else if (choice.FinishReason == CompletionsFinishReason.Stopped)
                {
                    // Mi assicuro di chiamare almeno una volta la funzione di query
                    if (anyFunctionCalled)
                    {
                        // Nessuna altra funzione, posso uscire
                        loop = false;
                    }
                    else
                    {
                        anyFunctionCalled = true;
                        await CallQueryFunctionAsync(request, options, string.Empty, token);
                    }

                }
            }
        }

        await foreach (var chatAppResponse in ProcessResponseAsync(response, token))
        {
            token.ThrowIfCancellationRequested();
            yield return chatAppResponse;
        }
    }

    private async Task CallFunctionAsync(
        ChatRequest chatRequest,
        ChatCompletionsOptions chatCompletionsOptions,
        string functionName,
        string arguments,
        CancellationToken token)
    {
        switch (functionName)
        {
            case EventsFunction:
                await CallEventsFunctionAsync(chatRequest, chatCompletionsOptions, arguments, token);
                break;
            default:
                await CallQueryFunctionAsync(chatRequest, chatCompletionsOptions, arguments, token);
                break;
        }
    }

    private async Task CallQueryFunctionAsync(
        ChatRequest chatRequest,
        ChatCompletionsOptions chatCompletionsOptions,
        string arguments,
        CancellationToken token)
    {
        _logger.LogInformation("Calling query function");

        if (chatCompletionsOptions.Messages[0].Content.EndsWith(AdditionalSystemMessage))
        {
            _logger.LogWarning("Query function already called");

            // Evito loop, sono già passato da questa funzione
            return;
        }

        var options = GetChatCompletionsOptions(chatRequest);
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
            throw new InvalidOperationException("No response");
        }

        var queryText = queryResponse.Value.Choices[0].Message.Content;
        // Se la query è 0, uso l'ultima domanda
        if (queryText is null or "0")
        {
            queryText = chatRequest.Messages[^1].Content;
        }
        _logger.LogInformation("Query text: {QueryText}", queryText);

        var embeddingResponse = await _openAiClient.GetEmbeddingsAsync(AdaModel, new(queryText), token);
        if (embeddingResponse is null)
        {
            throw new InvalidOperationException("No embedding response");
        }

        var queryVectors = embeddingResponse.Value.Data[0].Embedding.ToArray();
        var searchResponse = await _searchClient.SearchAsync<SearchDocument>(queryText, new()
        {
            Select = { "title", "text", "uri" },
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

        // Aggiorno istruzioni per gestire le fonti
        chatCompletionsOptions.Messages[0].Content += AdditionalSystemMessage;

        // Aggiungo le fonti
        //chatCompletionsOptions.Messages[^1].Content += $"\n\nFonti:\n{source}";
        // Aggiungo il messaggio in coda
        var message = new ChatMessage(ChatRole.Function, $"\n\nFonti:\n{source}") { Name = QueryFunction };
        chatCompletionsOptions.Messages.Add(message);
    }

    private async Task CallEventsFunctionAsync(
        ChatRequest chatRequest,
        ChatCompletionsOptions chatCompletionsOptions,
        string arguments,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        EventsArguments? eventsArguments = null;
        // Provo a trasformare il json
        try
        {
            eventsArguments = JsonSerializer.Deserialize<EventsArguments>(arguments, JsonSerializerOptions);
        }
        catch (JsonException)
        {
            
        }
        eventsArguments ??= new();

        _logger.LogInformation("Calling events function with arguments: {Arguments}", eventsArguments);

        // Decido gli intervalli di data
        // Oggi se non specificato
        var toDate = (eventsArguments.DateTime ?? DateTime.Today).Date;
        // Solo oggi se non specificato
        var fromDate = toDate.AddDays(-eventsArguments.DaysNumber.GetValueOrDefault(0));

        // Ciclo le data da fromDate a toDate
        var results = new List<EventItem>();
        while (fromDate <= toDate)
        {
            for (var i = 0; i < Random.Shared.Next(0, 5); i++)
            {
                var date = fromDate.AddHours(Random.Shared.Next(0, 23)).AddMinutes(Random.Shared.Next(0, 59));
                var description = EventNames[Random.Shared.Next(0, EventNames.Length)];
                results.Add(new EventItem(date.ToString("yyyy-MM-dd HH:mm"), description));
            }

            fromDate = fromDate.AddDays(1);
        }

        // Serializzo in json
        var content = JsonSerializer.Serialize(results, JsonSerializerOptions);

        // Aggiungo il messaggio in coda
        var message = new ChatMessage(ChatRole.Function, content) { Name = EventsFunction };
        chatCompletionsOptions.Messages.Add(message);
    }

    private IList<FunctionDefinition> GetFunctions()
    {
        return new List<FunctionDefinition>()
        {
            new(QueryFunction)
            {
                Description =
                    "Prepara una query di ricerca per ottenere le fonti necessarie a rispondere. Utilizza sempre questa funzione per ottenere le fonti",
                Parameters = BinaryData.FromObjectAsJson(new
                {
                    Type = "object",
                    Properties = new { },
                    Required = Array.Empty<string>(),
                }, new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            },
            new(EventsFunction)
            {
                Description =
                    "Restituisce la lista degli eventi (armato, disarmato, attivazione, disattivazione) su dispositivi (porte, sensori, finestre, sirena, presenze) dell'antifurto che si sono verificati nel periodo indicato.",
                Parameters = BinaryData.FromObjectAsJson(new
                {
                    Type = "object",
                    Properties = new
                    {
                        Days = new
                        {
                            Type = "string",
                            Description = "Numero di giorni antecedenti a oggi (ieri=1,l'altro ieri=2)",
                            Example = "1"
                        },
                        Date = new
                        {
                            Type = "string",
                            Description = "Data esatta di ricerca, formato ISO 8601",
                            Example = "2023-06-28"
                        }
                    },
                    Required = Array.Empty<string>(),
                }, new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            }
        };
    }

    private record EventItem(string Date, string Description);

    private record EventsArguments(string? Days = null, string? Date = null)
    {

        public int? DaysNumber
        {
            get
            {
                if (int.TryParse(Days, out var d))
                {
                    return d;
                }

                return null;
            }
        }

        public DateTime? DateTime
        {
            get
            {
                if (Date != null && System.DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                {
                    return d;
                }

                return null;
            }
        }
    }
}
