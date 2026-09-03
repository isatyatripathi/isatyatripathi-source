using System.Text.Json;

namespace DevSignalStudio.Domain.Configuration;

public sealed record TopicTaxonomyDocument
{
    public int SchemaVersion { get; init; } = 1;
    public TopicProfile Profile { get; init; } = new();
    public IReadOnlyList<TopicPillar> Pillars { get; init; } = Array.Empty<TopicPillar>();
}

public sealed record TopicProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double DefaultMinimumScore { get; init; } = 55;
    public int DailyCandidateLimit { get; init; } = 20;
    public int DraftCandidateLimit { get; init; } = 5;
}

public sealed record TopicPillar
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Priority { get; init; } = 3;
    public double Weight { get; init; } = 0.5;
    public IReadOnlyList<TopicKeyword> Keywords { get; init; } = Array.Empty<TopicKeyword>();
    public IReadOnlyList<string> Subtopics { get; init; } = Array.Empty<string>();
}

public sealed record TopicKeyword
{
    public string Term { get; init; } = string.Empty;
    public double Weight { get; init; } = 1;
}

public sealed record ContentRecipesDocument
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<ContentRecipe> Recipes { get; init; } = Array.Empty<ContentRecipe>();
}

public sealed record ContentRecipe
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Channel { get; init; } = "linkedin";
    public bool Enabled { get; init; } = true;
    public int? TargetCharacters { get; init; }
    public int? HardMaximumCharacters { get; init; }
    public int? TargetWords { get; init; }
    public int? HardMaximumWords { get; init; }
    public IReadOnlyList<string> Sections { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DiagramPreference { get; init; } = Array.Empty<string>();
    public HashtagRange? HashtagRange { get; init; }
    public IReadOnlyList<string> Requirements { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Voice { get; init; } = Array.Empty<string>();
}

public sealed record HashtagRange
{
    public int Min { get; init; }
    public int Max { get; init; }
}

public sealed record ProfileSettingsDocument
{
    public int SchemaVersion { get; init; } = 1;
    public AuthorProfile Profile { get; init; } = new();
    public ScheduleSettings Schedule { get; init; } = new();
    public StorageSettings Storage { get; init; } = new();
}

public sealed record AuthorProfile
{
    public string DisplayName { get; init; } = string.Empty;
    public string ProfessionalDirection { get; init; } = string.Empty;
    public IReadOnlyList<string> Audiences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Voice { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Avoid { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ContentPillars { get; init; } = Array.Empty<string>();
    public string DefaultLanguage { get; init; } = "en";
    public bool ManualPublishingOnly { get; init; } = true;
}

public sealed record ScheduleSettings
{
    public bool Enabled { get; init; }
    public string LocalTime { get; init; } = "07:00";
    public bool RunOnStartupWhenOverdue { get; init; } = true;
    public bool GenerateDrafts { get; init; } = true;
    public int MaximumRunsPerDay { get; init; } = 1;
}

public sealed record StorageSettings
{
    public string Mode { get; init; } = "JsonSnapshot";
    public string Directory { get; init; } = "data";
    public int BackupCount { get; init; } = 3;
}

public sealed record AiProvidersDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string DefaultRoute { get; init; } = "offline";
    public IReadOnlyList<AiProviderDefinition> Providers { get; init; } = Array.Empty<AiProviderDefinition>();
    public IReadOnlyList<AiRouteDefinition> Routes { get; init; } = Array.Empty<AiRouteDefinition>();
}

public sealed record AiProviderDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string? BaseUrl { get; init; }
    public string Model { get; init; } = string.Empty;
    public string? ApiKeyEnvironmentVariable { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, JsonElement> Settings { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AiRouteDefinition
{
    public string Id { get; init; } = string.Empty;
    public AiTaskRoutes Tasks { get; init; } = new();
}

public sealed record AiTaskRoutes
{
    public IReadOnlyList<string> Classify { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Draft { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Diagram { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ForTask(string task) => (task ?? "draft").ToLowerInvariant() switch
    {
        "classify" => Classify ?? Array.Empty<string>(),
        "diagram" => Diagram ?? Array.Empty<string>(),
        _ => Draft ?? Array.Empty<string>()
    };
}
