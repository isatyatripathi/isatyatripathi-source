using System.Text.Json;
using DevSignalStudio.Domain.Common;

namespace DevSignalStudio.Domain.Ai;

public sealed record AiRequest
{
    public string Task { get; init; } = "draft";
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserPrompt { get; init; } = string.Empty;
    public JsonElement Context { get; init; }
    public double Temperature { get; init; } = 0.3;
    public int MaxOutputTokens { get; init; } = 2_500;
}

public sealed record AiResponse
{
    public string Content { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public long DurationMilliseconds { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record RoutedAiResponse
{
    public AiResponse Response { get; init; } = new();
    public string RouteId { get; init; } = string.Empty;
    public IReadOnlyList<string> FallbackErrors { get; init; } = Array.Empty<string>();
}

public sealed record AiProviderHealth
{
    public string ProviderId { get; init; } = string.Empty;
    public HealthState Status { get; init; } = HealthState.Unknown;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
    public TimeSpan? Latency { get; init; }
}
