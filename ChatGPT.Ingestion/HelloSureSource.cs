using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

using Microsoft.SemanticKernel.Text;

namespace ChatGPT.Ingestion;

internal partial class HelloSureSource : ISource
{
    private readonly HashSet<Uri> _visitedUris = new();
    private readonly HtmlWeb _htmlWeb = new();
    [GeneratedRegex(@"[^a-zA-Z0-9_\-=]")]
    private static partial Regex IdRegex();

    public async IAsyncEnumerable<SourceItem> LoadAsync()
    {
        var uri = new Uri("https://www.hellosure.app/index.php/it/manuale");

        await foreach (var item in VisitPageAsync(uri, false))
        {
            yield return item;
        }

        uri = new Uri("https://www.hellosure.app/index.php/it/");

        await foreach (var item in VisitPageAsync(uri, true))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<SourceItem> VisitPageAsync(Uri uri, bool withContent)
    {
        // Se ho già visitato questa pagina, non la visito di nuovo
        if (_visitedUris.Contains(uri))
        {
            yield break;
        }

        // Solo le pagine in italiano del manuale
        if (uri.LocalPath.StartsWith("/index.php/en", StringComparison.OrdinalIgnoreCase)
            || uri.LocalPath.StartsWith("/index.php/es", StringComparison.OrdinalIgnoreCase)
            || uri.LocalPath.StartsWith("/index.php/mx", StringComparison.OrdinalIgnoreCase)
            || uri.LocalPath.StartsWith("/index.php/en-au", StringComparison.OrdinalIgnoreCase)
            || uri.LocalPath.StartsWith("/store/", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        _visitedUris.Add(uri);

        var htmlDoc = await _htmlWeb.LoadFromWebAsync(uri.ToString());

        if (withContent)
        {
            // Estrapolo il titolo della pagina
            var title = htmlDoc.DocumentNode.SelectSingleNode("//head/title")?.InnerText
                        ?? uri.LocalPath;

            // Estrapolo il testo della pagina
            var lines = ConvertTo(htmlDoc.DocumentNode).ToList();

            // Divido il testo in chunk di 1000 token
            const int maxTokens = 1000;
            var chunks = TextChunker.SplitPlainTextParagraphs(lines,
                maxTokens,
                (int)(maxTokens * 0.1));

            var c = 1;
            foreach (var chunk in chunks)
            {
                yield return new (
                    Id: $"{IdRegex().Replace(uri.LocalPath.ToLower(), "-")}-{c}",
                    Public: true,
                    Uri: uri,
                    Title: title,
                    Text: chunk);
                c++;
            }
        }

        var anchors = htmlDoc.DocumentNode.SelectNodes("//a");
        if (anchors is null)
        {
            yield break;
        }
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", null);
            if (href is null || !Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out var anchorUri))
            {
                continue;
            }

            // Se l'URI è relativo, lo rendo assoluto
            if (!anchorUri.IsAbsoluteUri)
            {
                anchorUri = new Uri(uri, anchorUri);
            }

            // Escludo altri domini
            if (anchorUri.Host != uri.Host)
            {
                continue;
            }

            // Ciclo ricorsivamente tutte le pagine
            await foreach (var item in VisitPageAsync(anchorUri, true))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<string> ConvertContentTo(HtmlNode node, StringBuilder line)
    {
        return node.ChildNodes
            .SelectMany(n => ConvertTo(n, line))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim());
    }

    private static IEnumerable<string> ConvertTo(HtmlNode node)
    {
        return ConvertTo(node, new());
    }

    private static IEnumerable<string> ConvertTo(HtmlNode node, StringBuilder line)
    {
        // Escludi gli elementi figli di <header> e <footer>
        if (node.ParentNode is { Name: "header" or "footer" })
        {
            yield break;
        }

        switch (node.NodeType)
        {
            case HtmlNodeType.Comment:
                break;
            case HtmlNodeType.Document:
                foreach (var l in ConvertContentTo(node, line))
                {
                    yield return l;
                }
                break;
            case HtmlNodeType.Text:
                string parentName = node.ParentNode.Name;
                if (parentName is "script" or "style" or "a")
                    break;

                var html = ((HtmlTextNode)node).Text;

                if (HtmlNode.IsOverlappedClosingElement(html))
                    break;

                if (html.Trim().Length > 0)
                {
                    var text = HtmlEntity.DeEntitize(html);
                    // Evito di aggiungere spazi inutili
                    if (line is [.., ' '])
                    {
                        text = text.TrimStart();
                    }
                    line.Append(text);
                }
                break;
            case HtmlNodeType.Element:
                switch (node.Name)
                {
                    case "p":
                    case "br":
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "h6":
                        yield return line.ToString();
                        line.Clear();
                        break;
                    case "li":
                        yield return line.ToString();
                        line.Clear();
                        line.Append("- ");
                        break;
                }

                if (node.HasChildNodes)
                {
                    foreach (var l in ConvertContentTo(node, line))
                    {
                        yield return l;
                    }
                }
                break;
        }
    }
}