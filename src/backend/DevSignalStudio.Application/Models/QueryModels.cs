using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Drafting;
using DevSignalStudio.Domain.Runs;

namespace DevSignalStudio.Application.Models;

public sealed record ContentQuery
{
    public string? Query { get; init; }
    public string? Topic { get; init; }
    public string? SourceId { get; init; }
    public double? MinimumScore { get; init; }
    public ContentItemStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string Sort { get; init; } = "score-desc";
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record DraftQuery
{
    public DraftStatus? Status { get; init; }
    public string? Channel { get; init; }
    public string? Topic { get; init; }
    public string? RecipeId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record RunQuery
{
    public RunStatus? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record ManualContentRequest
{
    public string SourceId { get; init; } = "manual-local";
    public string Title { get; init; } = string.Empty;
    public string? Url { get; init; }
    public string? Summary { get; init; }
    public string? Content { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Notes { get; init; }
}

public sealed record DraftEditRequest
{
    public int ExpectedRevision { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Hook { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public IReadOnlyList<string> Hashtags { get; init; } = Array.Empty<string>();
    public string? Mermaid { get; init; }
}

public sealed record DraftDecisionRequest
{
    public int ExpectedRevision { get; init; }
    public string? Reason { get; init; }
}

public sealed record MarkPublishedRequest
{
    public int ExpectedRevision { get; init; }
    public string Channel { get; init; } = "linkedin";
    public string? Url { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

public sealed record ExportArtifact(
    string FileName,
    string ContentType,
    string Content);

public sealed record SourceTestResult
{
    public DevSignalStudio.Domain.Sources.ConnectorHealth Health { get; init; } = new();
    public IReadOnlyList<RawContentItem> Preview { get; init; } = Array.Empty<RawContentItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record WorkspaceStatistics
{
    public int ItemCount { get; init; }
    public int CandidateCount { get; init; }
    public int DraftCount { get; init; }
    public int ReviewCount { get; init; }
    public int ApprovedCount { get; init; }
    public int PublishedCount { get; init; }
}

public sealed record DashboardSnapshot
{
    public WorkspaceStatistics Statistics { get; init; } = new();
    public IngestionRun? LatestIngestionRun { get; init; }
    public IReadOnlyDictionary<string, int> TopicDistribution { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> SevenDayActivity { get; init; }
        = new Dictionary<string, int>();
    public int EnabledSourceCount { get; init; }
    public int EnabledProviderCount { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record MermaidSanitizationResult
{
    public bool IsValid { get; init; }
    public string Sanitized { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
