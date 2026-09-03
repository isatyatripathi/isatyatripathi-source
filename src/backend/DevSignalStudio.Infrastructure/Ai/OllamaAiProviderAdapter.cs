using System.Diagnostics;
using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;

namespace DevSignalStudio.Infrastructure.Ai;

public sealed class OllamaAiProviderAdapter : IAiProviderAdapter
{
    private readonly AiHttpClient _http;
    private readonly IClock _clock;

    public OllamaAiProviderAdapter(AiHttpClient http, IClock clock)
    {
        _http = http;
        _clock = clock;
    }

    public string ProviderType => "ollama";

    public async Task<AiResponse> GenerateAsync(
        AiProviderDefinition provider,
        AiRequest request,
        CancellationToken cancellationToken)
    {
        Validate(provider);
        Stopwatch stopwatch = Stopwatch.StartNew();
        string url = Combine(provider.BaseUrl!, "/api/chat");
        object body = new
        {
            model = provider.Model,
            stream = false,
            format = "json",
            keep_alive = provider.GetString("keepAlive") ?? "10m",
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            options = new { temperature = request.Temperature }
        };
        using JsonDocument response = await _http.PostAsync(
            url,
            body,
            null,
            TimeSpan.FromSeconds(provider.GetInt("timeoutSeconds", 120)),
            cancellationToken);
        JsonElement root = response.RootElement;
        string content = root.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        return new AiResponse
        {
            Content = content,
            ProviderId = provider.Id,
            ProviderType = provider.Type,
            Model = provider.Model,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            InputTokens = TryInt(root, "prompt_eval_count"),
            OutputTokens = TryInt(root, "eval_count")
        };
    }

    public async Task<AiProviderHealth> CheckHealthAsync(
        AiProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        if (!provider.Enabled)
        {
            return Disabled(provider.Id);
        }
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            Validate(provider);
            using JsonDocument response = await _http.GetAsync(
                Combine(provider.BaseUrl!, "/api/tags"),
                null,
                TimeSpan.FromSeconds(10),
                cancellationToken);
            return new AiProviderHealth
            {
                ProviderId = provider.Id,
                Status = HealthState.Healthy,
                Message = "Ollama is reachable.",
                CheckedAt = _clock.UtcNow,
                Latency = stopwatch.Elapsed
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unhealthy(provider.Id, exception.Message, stopwatch.Elapsed);
        }
    }

    private static void Validate(AiProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(provider.Model))
        {
            throw new InvalidOperationException("Ollama requires baseUrl and model.");
        }
    }

    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;

    private static int? TryInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;

    private AiProviderHealth Disabled(string id) => new()
    {
        ProviderId = id,
        Status = HealthState.Disabled,
        Message = "The provider is disabled.",
        CheckedAt = _clock.UtcNow
    };

    private AiProviderHealth Unhealthy(string id, string message, TimeSpan latency) => new()
    {
        ProviderId = id,
        Status = HealthState.Unhealthy,
        Message = message,
        CheckedAt = _clock.UtcNow,
        Latency = latency
    };
}
