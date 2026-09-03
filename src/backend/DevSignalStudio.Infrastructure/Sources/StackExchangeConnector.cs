using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;
using DevSignalStudio.Infrastructure.Security;

namespace DevSignalStudio.Infrastructure.Sources;

public sealed class StackExchangeConnector : IContentConnector
{
    private readonly SafeHttpFetcher _http;
    private readonly IClock _clock;

    public StackExchangeConnector(SafeHttpFetcher http, IClock clock)
    {
        _http = http;
        _clock = clock;
    }

    public string ConnectorType => "stackexchange";

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
                    ? "The Stack Exchange API returned at least one question."
                    : "The API was reachable but returned no questions.",
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
            throw new InvalidOperationException("A Stack Exchange API endpoint is required.");
        }

        Uri uri = BuildUri(source);
        string json = await _http.GetStringAsync(uri.AbsoluteUri, cancellationToken);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        List<string> warnings = new();
        DateTimeOffset? retryAfter = null;

        if (root.TryGetProperty("backoff", out JsonElement backoff) && backoff.TryGetInt32(out int seconds))
        {
            retryAfter = _clock.UtcNow.AddSeconds(seconds);
            warnings.Add($"Stack Exchange requested a {seconds}-second backoff.");
        }
        if (root.TryGetProperty("quota_remaining", out JsonElement quota) && quota.TryGetInt32(out int remaining) && remaining < 100)
        {
            warnings.Add($"Stack Exchange quota is low ({remaining} requests remaining). ");
        }
        if (!root.TryGetProperty("items", out JsonElement itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The Stack Exchange API response did not contain an items array.");
        }

        int limit = Math.Clamp(source.MaxItemsPerRun ?? 40, 1, 100);
        List<RawContentItem> items = new();
        foreach (JsonElement question in itemsElement.EnumerateArray().Take(limit))
        {
            string id = question.TryGetProperty("question_id", out JsonElement idElement)
                ? idElement.ToString()
                : string.Empty;
            string title = question.TryGetProperty("title", out JsonElement titleElement)
                ? WebUtility.HtmlDecode(titleElement.GetString() ?? string.Empty)
                : string.Empty;
            string? link = question.TryGetProperty("link", out JsonElement linkElement)
                ? linkElement.GetString()
                : null;
            string? owner = null;
            if (question.TryGetProperty("owner", out JsonElement ownerElement) &&
                ownerElement.TryGetProperty("display_name", out JsonElement ownerName))
            {
                owner = WebUtility.HtmlDecode(ownerName.GetString() ?? string.Empty);
            }

            int score = TryInt(question, "score");
            int answers = TryInt(question, "answer_count");
            int views = TryInt(question, "view_count");
            bool answered = question.TryGetProperty("is_answered", out JsonElement answeredElement) &&
                answeredElement.ValueKind == JsonValueKind.True;
            string summary = $"Community question with score {score}, {answers} answers, {views} views; answered: {answered}.";
            string[] tags = question.TryGetProperty("tags", out JsonElement tagsElement) &&
                tagsElement.ValueKind == JsonValueKind.Array
                ? tagsElement.EnumerateArray()
                    .Select(tag => tag.GetString())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag!)
                    .ToArray()
                : Array.Empty<string>();
            DateTimeOffset? published = question.TryGetProperty("creation_date", out JsonElement created) &&
                created.TryGetInt64(out long unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : null;

            if (!string.IsNullOrWhiteSpace(title))
            {
                items.Add(new RawContentItem
                {
                    ExternalId = string.IsNullOrWhiteSpace(id) ? link ?? title : id,
                    Title = title,
                    Url = link,
                    Summary = summary,
                    Author = owner,
                    PublishedAt = published,
                    Tags = tags
                });
            }
        }

        return new ConnectorFetchResult
        {
            Items = items,
            Warnings = warnings,
            RetryAfter = retryAfter
        };
    }

    private static Uri BuildUri(SourceDefinition source)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["site"] = source.GetString("site") ?? "stackoverflow",
            ["sort"] = source.GetString("sort") ?? "activity",
            ["order"] = source.GetString("order") ?? "desc",
            ["filter"] = source.GetString("filter") ?? "default",
            ["pagesize"] = Math.Clamp(source.MaxItemsPerRun ?? 40, 1, 100).ToString()
        };
        string? tagged = source.GetString("tagged");
        if (!string.IsNullOrWhiteSpace(tagged))
        {
            values["tagged"] = tagged;
        }

        UriBuilder builder = new(source.Endpoint!);
        builder.Query = string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri;
    }

    private static int TryInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int number)
            ? number
            : 0;
}
