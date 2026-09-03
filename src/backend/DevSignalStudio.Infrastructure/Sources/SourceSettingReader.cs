using System.Text.Json;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Infrastructure.Sources;

internal static class SourceSettingReader
{
    public static string? GetString(this SourceDefinition source, string key)
    {
        if (source.ConnectorSettings is null ||
            !source.ConnectorSettings.TryGetValue(key, out JsonElement value))
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
}
