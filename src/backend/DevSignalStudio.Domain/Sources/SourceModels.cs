using System.Text.Json;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;

namespace DevSignalStudio.Domain.Sources;

public sealed record SourceDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ConnectorType { get; init; } = string.Empty;
    public string? Endpoint { get; init; }
    public bool Enabled { get; init; }
    public double TrustWeight { get; init; } = 0.5;
    public IReadOnlyList<string> DefaultTags { get; init; } = Array.Empty<string>();
    public int? PollMinutes { get; init; }
    public int? MaxItemsPerRun { get; init; }
    public IReadOnlyDictionary<string, JsonElement> ConnectorSettings { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    public string? ComplianceNotes { get; init; }
}

public sealed record SourcesDocument
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<SourceDefinition> Sources { get; init; } = Array.Empty<SourceDefinition>();
}

public sealed record ConnectorHealth
{
    public HealthState Status { get; init; } = HealthState.Unknown;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
    public TimeSpan? Latency { get; init; }
}

public sealed record ConnectorFetchResult
{
    public IReadOnlyList<RawContentItem> Items { get; init; } = Array.Empty<RawContentItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public DateTimeOffset? RetryAfter { get; init; }
}

public sealed record SourceFetchSummary
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public RunStatus Status { get; init; } = RunStatus.Queued;
    public int Fetched { get; init; }
    public int Added { get; init; }
    public int Duplicates { get; init; }
    public int Candidates { get; init; }
    public int Archived { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
