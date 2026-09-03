using System.Diagnostics;
using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;

namespace DevSignalStudio.Infrastructure.Ai;

public sealed class AnthropicAiProviderAdapter : IAiProviderAdapter
{
    private readonly AiHttpClient _http;
    private readonly IClock _clock;

    public AnthropicAiProviderAdapter(AiHttpClient http, IClock clock)
    {
        _http = http;
        _clock = clock;
    }

    public string ProviderType => "anthropic";

    public async Task<AiResponse> GenerateAsync(
        AiProviderDefinition provider,
        AiRequest request,
        CancellationToken cancellationToken)
    {
        Validate(provider);
        Stopwatch stopwatch = Stopwatch.StartNew();

        object body = new
        {
            model = provider.Model,
            max_tokens = request.MaxOutputTokens,
            temperature = request.Temperature,
            system = request.SystemPrompt,
            messages = new object[]
            {
                new { role = "user", content = request.UserPrompt }
            }
        };

        using JsonDocument response = await _http.PostAsync(
            Combine(provider.BaseUrl!, "/messages"),
            body,
            BuildHeaders(provider),
            TimeSpan.FromSeconds(provider.GetInt("timeoutSeconds", 90)),
            cancellationToken);

        JsonElement root = response.RootElement;
        string content = ReadContent(root);
        JsonElement usage = root.TryGetProperty("usage", out JsonElement usageValue)
            ? usageValue
            : default;

        return new AiResponse
        {
            Content = content,
            ProviderId = provider.Id,
            ProviderType = provider.Type,
            Model = ReadString(root, "model") ?? provider.Model,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            InputTokens = TryInt(usage, "input_tokens"),
            OutputTokens = TryInt(usage, "output_tokens")
        };
    }

    public async Task<AiProviderHealth> CheckHealthAsync(
        AiProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        if (!provider.Enabled)
        {
            return BuildHealth(provider.Id, HealthState.Disabled, "The provider is disabled.", null);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            Validate(provider);
            AiRequest probe = new()
            {
                Task = "health",
                SystemPrompt = "Respond with valid JSON only.",
                UserPrompt = "Return {\"ok\":true}.",
                MaxOutputTokens = 20,
                Temperature = 0
            };
            await GenerateAsync(provider, probe, cancellationToken);
            return BuildHealth(
                provider.Id,
                HealthState.Healthy,
                $"{provider.DisplayName} is reachable.",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BuildHealth(provider.Id, HealthState.Unhealthy, exception.Message, stopwatch.Elapsed);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildHeaders(AiProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKeyEnvironmentVariable))
        {
            throw new InvalidOperationException("Anthropic requires an API key environment variable.");
        }

        string? apiKey = Environment.GetEnvironmentVariable(provider.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Environment variable '{provider.ApiKeyEnvironmentVariable}' is not set.");
        }

        return new Dictionary<string, string>
        {
            ["x-api-key"] = apiKey,
            ["anthropic-version"] = provider.GetString("apiVersion") ?? "2023-06-01"
        };
    }

    private static string ReadContent(JsonElement root)
    {
        if (!root.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The Anthropic response did not contain content.");
        }

        string value = string.Join(
            "\n",
            content.EnumerateArray()
                .Where(part =>
                    part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("type", out JsonElement type) &&
                    type.GetString() == "text")
                .Select(part =>
                    part.TryGetProperty("text", out JsonElement text)
                        ? text.GetString()
                        : null)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("The Anthropic response contained no text blocks.");
        }
        return value;
    }

    private static void Validate(AiProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            throw new InvalidOperationException("Anthropic requires baseUrl.");
        }
        if (string.IsNullOrWhiteSpace(provider.Model) ||
            provider.Model.Equals("configure-me", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Anthropic requires a configured model.");
        }
    }

    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? TryInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.TryGetInt32(out int result)
            ? result
            : null;

    private AiProviderHealth BuildHealth(
        string providerId,
        HealthState state,
        string message,
        TimeSpan? latency) => new()
    {
        ProviderId = providerId,
        Status = state,
        Message = message,
        CheckedAt = _clock.UtcNow,
        Latency = latency
    };
}
