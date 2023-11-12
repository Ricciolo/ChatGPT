namespace ChatGPT.Ingestion;

internal interface ISource
{
    IAsyncEnumerable<SourceItem> LoadAsync();
}