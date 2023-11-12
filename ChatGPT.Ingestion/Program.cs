using System.Runtime.CompilerServices;

using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

using ChatGPT.Ingestion;

using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder();

builder.Configuration.AddUserSecrets<Program>();
builder.Services.AddAzureClients(b =>
{
    b.AddSearchIndexClient(builder.Configuration.GetSection("Search"));
    b.AddSearchClient(builder.Configuration.GetSection("Search"));
    b.AddOpenAIClient(builder.Configuration.GetSection("OpenAI"));
});
builder.Services.AddSingleton<ISource, HelloSureSource>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();

const int modelDimensions = 1536;
const string algorithmName = "hnsw";
const string indexName = "hellosure";
const string engine = "ada";

var searchIndexClient = host.Services.GetRequiredService<SearchIndexClient>();
var searchClient = host.Services.GetRequiredService<SearchClient>();
var openAiClient = host.Services.GetRequiredService<OpenAIClient>();

await PrepareIndexAsync();

await IndexSourcesAsync();
async Task IndexSourcesAsync()
{
    var chunk = new List<SourceItem>();
    foreach (var source in host.Services.GetServices<ISource>())
    {
        await foreach (var item in source.LoadAsync())
        {
            chunk.Add(item);
            logger.LogInformation("Loaded {uri}", item.Uri);

            if (chunk.Count == 16)
            {
                await IndexChunkAsync(chunk);
                chunk.Clear();
            }
        }
    }

    if (chunk.Count > 0)
    {
        await IndexChunkAsync(chunk);
    }
}

async Task IndexChunkAsync(IReadOnlyList<SourceItem> chunk)
{
    // Estrapolo tutti i testi
    var inputs = chunk.Select(i => i.Text).ToArray();
    // Calcolo gli embeddings
    var embeddings = await openAiClient.GetEmbeddingsAsync(engine, new(inputs));
    if (embeddings is null)
    {
        throw new InvalidOperationException("Embeddings is null");
    }
    if (embeddings.Value.Data.Count != chunk.Count)
    {
        throw new InvalidOperationException("Embeddings count mismatch");
    }

    // Creo i documenti da indicizzare
    var documents = chunk.Select((s, i) => new SearchDocument
    {
        ["id"] = s.Id,
        ["public"] = s.Public,
        ["uri"] = s.Uri.ToString(),
        ["title"] = s.Title,
        ["text"] = s.Text,
        ["vector"] = embeddings.Value.Data[i].Embedding.ToArray()
    }).ToArray();

    logger.LogInformation("Indexing {Count} items", documents.Length);
    await searchClient.IndexDocumentsAsync(IndexDocumentsBatch.Upload(documents));
}

async Task PrepareIndexAsync()
{
    var indexExists = searchIndexClient.GetIndexNames().Any(n => n == indexName);
    if (indexExists)
    {
        logger.LogInformation("Deleting index");
        await searchIndexClient.DeleteIndexAsync(indexName);
    }

    var definition = new SearchIndex(indexName)
    {
        VectorSearch = new()
        {
            AlgorithmConfigurations =
            {
                new HnswVectorSearchAlgorithmConfiguration(algorithmName)
            }
        },
        Fields =
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
            new SimpleField("public", SearchFieldDataType.Boolean) { IsFilterable = true },
            new SimpleField("uri", SearchFieldDataType.String) { IsFilterable = true, IsSortable = true },
            new SearchableField("title") { AnalyzerName = LexicalAnalyzerName.ItLucene },
            new SearchableField("text") { AnalyzerName = LexicalAnalyzerName.ItLucene },
            new SearchField("vector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = modelDimensions,
                VectorSearchConfiguration = algorithmName
            }
        }
    };

    logger.LogInformation("Creating index {Name}", indexName);
    await searchIndexClient.CreateIndexAsync(definition);
}