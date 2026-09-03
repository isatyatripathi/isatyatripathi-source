using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Domain.Runs;

public sealed record IngestionRunRequest
{
    public IReadOnlyList<string> SourceIds { get; init; } = Array.Empty<string>();
    public bool Force { get; init; }
    public bool GenerateDrafts { get; init; }
    public int MaxCandidates { get; init; } = 20;
}

public sealed record IngestionRun
{
    public string Id { get; init; } = string.Empty;
    public RunStatus Status { get; init; } = RunStatus.Queued;
    public string Trigger { get; init; } = "manual";
    public IngestionRunRequest Request { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int Fetched { get; init; }
    public int Added { get; init; }
    public int Duplicates { get; init; }
    public int Candidates { get; init; }
    public int DraftsQueued { get; init; }
    public IReadOnlyList<SourceFetchSummary> Sources { get; init; } = Array.Empty<SourceFetchSummary>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed record DraftGenerationRequest
{
    public IReadOnlyList<string> ContentItemIds { get; init; } = Array.Empty<string>();
    public string RecipeId { get; init; } = "linkedin-explainer";
    public string? ProviderRoute { get; init; }
    public string? Instructions { get; init; }
}

public sealed record DraftGenerationRun
{
    public string Id { get; init; } = string.Empty;
    public RunStatus Status { get; init; } = RunStatus.Queued;
    public DraftGenerationRequest Request { get; init; } = new();
    public string? DraftId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
