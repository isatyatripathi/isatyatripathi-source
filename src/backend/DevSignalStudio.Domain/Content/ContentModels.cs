using DevSignalStudio.Domain.Common;

namespace DevSignalStudio.Domain.Content;

public sealed record RawContentItem
{
    public string ExternalId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Url { get; init; }
    public string? Summary { get; init; }
    public string? Content { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Notes { get; init; }
    public string? RawPayloadHash { get; init; }
}

public sealed record ContentItem
{
    public string Id { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Url { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? Summary { get; init; }
    public string? Content { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset CollectedAt { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Notes { get; init; }
    public string ContentFingerprint { get; init; } = string.Empty;
    public ContentItemStatus Status { get; init; } = ContentItemStatus.Collected;
    public ContentScore Score { get; init; } = new();
    public IReadOnlyList<TopicMatch> TopicMatches { get; init; } = Array.Empty<TopicMatch>();
    public ContentProvenance Provenance { get; init; } = new();
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record ContentScore
{
    public double FinalScore { get; init; }
    public double TopicRelevance { get; init; }
    public double Freshness { get; init; }
    public double SourceAuthority { get; init; }
    public double LearningValue { get; init; }
    public double CareerAlignment { get; init; }
    public double Novelty { get; init; }
    public double DiscussionPotential { get; init; }
    public double DuplicatePenalty { get; init; }
    public double HypePenalty { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

public sealed record TopicMatch
{
    public string PillarId { get; init; } = string.Empty;
    public string PillarName { get; init; } = string.Empty;
    public double Score { get; init; }
    public IReadOnlyList<string> MatchedTerms { get; init; } = Array.Empty<string>();
}

public sealed record ContentProvenance
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string ConnectorType { get; init; } = string.Empty;
    public string? OriginalUrl { get; init; }
    public string? ComplianceNotes { get; init; }
    public DateTimeOffset CollectedAt { get; init; }
}

public sealed record CuratedItemsDocument
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<CuratedItem> Items { get; init; } = Array.Empty<CuratedItem>();
}

public sealed record CuratedItem
{
    public string Id { get; init; } = string.Empty;
    public string? SourceName { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Url { get; init; }
    public string? Summary { get; init; }
    public string? Content { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Notes { get; init; }
}
