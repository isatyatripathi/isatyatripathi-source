using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;
using DevSignalStudio.Infrastructure.Security;

namespace DevSignalStudio.Infrastructure.Sources;

public sealed partial class RssConnector : IContentConnector
{
    private readonly SafeHttpFetcher _http;
    private readonly IClock _clock;

    public RssConnector(SafeHttpFetcher http, IClock clock)
    {
        _http = http;
        _clock = clock;
    }

    public string ConnectorType => "rss";

    public async Task<ConnectorHealth> CheckHealthAsync(
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ConnectorFetchResult result = await FetchAsync(
                source with { MaxItemsPerRun = 1 },
                cancellationToken);
            return new ConnectorHealth
            {
                Status = result.Items.Count > 0 ? HealthState.Healthy : HealthState.Degraded,
                Message = result.Items.Count > 0
                    ? "The feed returned at least one item."
                    : "The feed was readable but contained no items.",
                CheckedAt = _clock.UtcNow,
                Latency = stopwatch.Elapsed
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ConnectorHealth
            {
                Status = HealthState.Unhealthy,
                Message = exception.Message,
                CheckedAt = _clock.UtcNow,
                Latency = stopwatch.Elapsed
            };
        }
    }

    public async Task<ConnectorFetchResult> FetchAsync(
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.Endpoint))
        {
            throw new InvalidOperationException("An RSS or Atom endpoint is required.");
        }

        string xml = await _http.GetStringAsync(source.Endpoint, cancellationToken);
        XDocument document = ParseSecurely(xml);
        int limit = Math.Clamp(source.MaxItemsPerRun ?? 20, 1, 500);
        IReadOnlyList<RawContentItem> items = IsAtom(document)
            ? ParseAtom(document, limit)
            : ParseRss(document, limit);

        return new ConnectorFetchResult
        {
            Items = items,
            Warnings = items.Count == 0
                ? new[] { "The feed parsed successfully but contained no supported items." }
                : Array.Empty<string>()
        };
    }

    private static XDocument ParseSecurely(string xml)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_500_000,
            IgnoreComments = true
        };
        using StringReader text = new(xml);
        using XmlReader reader = XmlReader.Create(text, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static bool IsAtom(XDocument document) =>
        document.Root?.Name.LocalName.Equals("feed", StringComparison.OrdinalIgnoreCase) == true;

    private static IReadOnlyList<RawContentItem> ParseRss(XDocument document, int limit)
    {
        return document.Descendants()
            .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item =>
            {
                string title = Text(item, "title");
                string link = Text(item, "link");
                string id = FirstNonEmpty(Text(item, "guid"), link, title);
                string description = FirstNonEmpty(
                    Text(item, "encoded"),
                    Text(item, "description"),
                    Text(item, "content"));
                string author = FirstNonEmpty(Text(item, "creator"), Text(item, "author"));
                string date = FirstNonEmpty(Text(item, "pubDate"), Text(item, "published"), Text(item, "updated"));
                string[] tags = item.Elements()
                    .Where(element => element.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))
                    .Select(element => Collapse(StripHtml(element.Value)))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new RawContentItem
                {
                    ExternalId = id,
                    Title = Collapse(StripHtml(title)),
                    Url = string.IsNullOrWhiteSpace(link) ? null : link.Trim(),
                    Summary = Truncate(Collapse(StripHtml(description)), 4_000),
                    Author = string.IsNullOrWhiteSpace(author) ? null : Collapse(StripHtml(author)),
                    PublishedAt = ParseDate(date),
                    Tags = tags
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToArray();
    }

    private static IReadOnlyList<RawContentItem> ParseAtom(XDocument document, int limit)
    {
        return document.Descendants()
            .Where(element => element.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(entry =>
            {
                string title = Text(entry, "title");
                XElement? linkElement = entry.Elements()
                    .FirstOrDefault(element =>
                    {
                        if (!element.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        string? relation = element.Attribute("rel")?.Value;
                        return string.IsNullOrWhiteSpace(relation) ||
                            relation.Equals("alternate", StringComparison.OrdinalIgnoreCase);
                    });
                string link = linkElement?.Attribute("href")?.Value ?? linkElement?.Value ?? string.Empty;
                string id = FirstNonEmpty(Text(entry, "id"), link, title);
                string description = FirstNonEmpty(Text(entry, "summary"), Text(entry, "content"));
                string author = entry.Elements()
                    .FirstOrDefault(element => element.Name.LocalName.Equals("author", StringComparison.OrdinalIgnoreCase))?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?
                    .Value ?? string.Empty;
                string date = FirstNonEmpty(Text(entry, "published"), Text(entry, "updated"));
                string[] tags = entry.Elements()
                    .Where(element => element.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))
                    .Select(element => element.Attribute("term")?.Value ?? element.Value)
                    .Select(Collapse)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new RawContentItem
                {
                    ExternalId = id,
                    Title = Collapse(StripHtml(title)),
                    Url = string.IsNullOrWhiteSpace(link) ? null : link.Trim(),
                    Summary = Truncate(Collapse(StripHtml(description)), 4_000),
                    Author = string.IsNullOrWhiteSpace(author) ? null : Collapse(StripHtml(author)),
                    PublishedAt = ParseDate(date),
                    Tags = tags
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToArray();
    }

    private static string Text(XElement parent, string localName) =>
        parent.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?
            .Value ?? string.Empty;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out DateTimeOffset date) ? date : null;

    private static string StripHtml(string value)
    {
        string withoutScripts = ScriptRegex().Replace(value, " ");
        return WebUtility.HtmlDecode(TagRegex().Replace(withoutScripts, " "));
    }

    private static string Collapse(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    private static string? Truncate(string value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maximum ? value : value[..maximum] + "...";

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
