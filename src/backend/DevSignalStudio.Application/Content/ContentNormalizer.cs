using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Content;

public sealed class ContentNormalizer
{
    private readonly IClock _clock;

    public ContentNormalizer(IClock clock)
    {
        _clock = clock;
    }

    public ContentItem Normalize(RawContentItem raw, SourceDefinition source)
    {
        string title = ContentIdentity.CollapseWhitespace(raw.Title);
        string? summary = NullIfEmpty(ContentIdentity.CollapseWhitespace(raw.Summary));
        string? content = NullIfEmpty(ContentIdentity.CollapseWhitespace(raw.Content));
        string? canonicalUrl = ContentIdentity.CanonicalizeUrl(raw.Url);
        string fingerprint = ContentIdentity.CreateFingerprint(title, summary, content);
        string externalId = string.IsNullOrWhiteSpace(raw.ExternalId)
            ? canonicalUrl ?? fingerprint
            : raw.ExternalId.Trim();
        DateTimeOffset now = _clock.UtcNow;

        string[] tags = (source.DefaultTags ?? Array.Empty<string>())
            .Concat(raw.Tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ContentItem
        {
            Id = ContentIdentity.CreateContentId(source.Id, externalId, canonicalUrl, fingerprint),
            SourceId = source.Id,
            SourceName = source.Name,
            ExternalId = externalId,
            Title = title,
            Url = NullIfEmpty(raw.Url?.Trim()),
            CanonicalUrl = canonicalUrl,
            Summary = summary,
            Content = content,
            Author = NullIfEmpty(ContentIdentity.CollapseWhitespace(raw.Author)),
            PublishedAt = raw.PublishedAt,
            CollectedAt = now,
            Tags = tags,
            Notes = NullIfEmpty(ContentIdentity.CollapseWhitespace(raw.Notes)),
            ContentFingerprint = fingerprint,
            Provenance = new ContentProvenance
            {
                SourceId = source.Id,
                SourceName = source.Name,
                ConnectorType = source.ConnectorType,
                OriginalUrl = raw.Url,
                ComplianceNotes = source.ComplianceNotes,
                CollectedAt = now
            }
        };
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
