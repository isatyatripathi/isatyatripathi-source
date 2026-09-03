using System.Diagnostics;
using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;

namespace DevSignalStudio.Infrastructure.Ai;

/// <summary>
/// Adapter for OpenAI's Chat Completions-compatible HTTP contract. The same
/// implementation is used for the official endpoint and configurable local or
/// hosted endpoints that expose an OpenAI-compatible API.
/// </summary>
public sealed class OpenAiChatProviderAdapter : IAiProviderAdapter
{
    private readonly AiHttpClient _http;
    private readonly IClock _clock;
    private readonly string _providerType;
    private readonly bool _forceJsonMode;

    public OpenAiChatProviderAdapter(
        AiHttpClient http,
        IClock clock,
        string providerType = "openai",
        bool forceJsonMode = true)
    {
        _http = http;
        _clock = clock;
        _providerType = providerType;
        _forceJsonMode = forceJsonMode;
    }

    public string ProviderType => _providerType;

    public async Task<AiResponse> GenerateAsync(
        AiProviderDefinition provider,
        AiRequest request,
        CancellationToken cancellationToken)
    {
        Validate(provider);
        Stopwatch stopwatch = Stopwatch.StartNew();

        Dictionary<string, object?> body = new()
        {
            ["model"] = provider.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxOutputTokens
        };

        if (_forceJsonMode || provider.GetBool("jsonMode", false))
        {
            body["response_format"] = new { type = "json_object" };
        }

        using JsonDocument response = await _http.PostAsync(
            Combine(provider.BaseUrl!, "/chat/completions"),
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
            InputTokens = TryInt(usage, "prompt_tokens"),
            OutputTokens = TryInt(usage, "completion_tokens")
        };
    }

    public async Task<AiProviderHealth> CheckHealthAsync(
        AiProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        if (!provider.Enabled)
        {
            return HealthStateFor(provider.Id, HealthState.Disabled, "The provider is disabled.", null);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            Validate(provider);

            // A model listing is a read-only and inexpensive readiness check for
            // official and most compatible endpoints. Some compatible servers do
            // not implement it, so those endpoints may opt into a generation check.
            if (provider.GetBool("healthUseGeneration", false))
            {
                AiRequest probe = new()
                {
                    Task = "health",
                    SystemPrompt = "Return JSON only.",
                    UserPrompt = "Return {\"ok\":true}.",
                    MaxOutputTokens = 20,
                    Temperature = 0
                };
                await GenerateAsync(provider, probe, cancellationToken);
            }
            else
            {
                using JsonDocument _ = await _http.GetAsync(
                    Combine(provider.BaseUrl!, "/models"),
                    BuildHeaders(provider),
                    TimeSpan.FromSeconds(provider.GetInt("healthTimeoutSeconds", 15)),
                    cancellationToken);
            }

            return HealthStateFor(
                provider.Id,
                HealthState.Healthy,
                $"{provider.DisplayName} is reachable.",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthStateFor(provider.Id, HealthState.Unhealthy, exception.Message, stopwatch.Elapsed);
        }
    }

    private static string ReadContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidDataException("The AI response did not contain choices.");
        }

        JsonElement first = choices[0];
        if (!first.TryGetProperty("message", out JsonElement message) ||
            !message.TryGetProperty("content", out JsonElement content))
        {
            throw new InvalidDataException("The AI response did not contain message content.");
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        // A few compatible endpoints return content parts rather than a string.
        if (content.ValueKind == JsonValueKind.Array)
        {
            return string.Join(
                "\n",
                content.EnumerateArray()
                    .Select(part =>
                        part.ValueKind == JsonValueKind.String
                            ? part.GetString()
                            : part.TryGetProperty("text", out JsonElement text)
                                ? text.GetString()
                                : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        throw new InvalidDataException("The AI response message content had an unsupported shape.");
    }

    private static IReadOnlyDictionary<string, string>? BuildHeaders(AiProviderDefinition provider)
    {
        string? apiKey = ReadApiKey(provider, required: provider.Type.Equals("openai", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {apiKey}"
        };
    }

    private static string? ReadApiKey(AiProviderDefinition provider, bool required)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKeyEnvironmentVariable))
        {
            if (required)
            {
                throw new InvalidOperationException("An API key environment variable must be configured.");
            }
            return null;
        }

        string? apiKey = Environment.GetEnvironmentVariable(provider.ApiKeyEnvironmentVariable);
        if (required && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Environment variable '{provider.ApiKeyEnvironmentVariable}' is not set.");
        }
        return apiKey;
    }

    private static void Validate(AiProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            throw new InvalidOperationException($"AI provider '{provider.Id}' requires baseUrl.");
        }
        if (string.IsNullOrWhiteSpace(provider.Model) ||
            provider.Model.Equals("configure-me", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AI provider '{provider.Id}' requires a configured model.");
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

    private AiProviderHealth HealthStateFor(
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
