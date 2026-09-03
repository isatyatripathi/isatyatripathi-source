using System.Text.Json;
using DevSignalStudio.Domain.Configuration;

namespace DevSignalStudio.Infrastructure.Ai;

internal static class ProviderSettingReader
{
    public static string? GetString(this AiProviderDefinition provider, string key)
    {
        if (provider.Settings is null || !provider.Settings.TryGetValue(key, out JsonElement value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null
        };
    }

    public static int GetInt(this AiProviderDefinition provider, string key, int fallback) =>
        provider.Settings is not null &&
        provider.Settings.TryGetValue(key, out JsonElement value) &&
        value.TryGetInt32(out int result)
            ? result
            : fallback;

    public static bool GetBool(this AiProviderDefinition provider, string key, bool fallback) =>
        provider.Settings is not null &&
        provider.Settings.TryGetValue(key, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
