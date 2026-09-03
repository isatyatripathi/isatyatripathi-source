using System.Text.Json;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Drafting;

namespace DevSignalStudio.Application.Drafting;

public sealed class DraftOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public DraftAiOutput Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainRuleException("ai_empty_output", "The AI provider returned an empty response.");
        }

        string json = ExtractJson(content);
        DraftAiOutput? result;
        try
        {
            result = JsonSerializer.Deserialize<DraftAiOutput>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new DomainRuleException(
                "ai_invalid_json",
                $"The AI provider did not return the required JSON shape: {exception.Message}");
        }

        if (result is null || string.IsNullOrWhiteSpace(result.Body))
        {
            throw new DomainRuleException("ai_missing_body", "The generated draft has no body.");
        }

        return result;
    }

    private static string ExtractJson(string content)
    {
        string value = content.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineEnd = value.IndexOf('\n');
            int closingFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && closingFence > firstLineEnd)
            {
                value = value[(firstLineEnd + 1)..closingFence].Trim();
            }
        }

        int start = value.IndexOf('{');
        int end = value.LastIndexOf('}');
        return start >= 0 && end > start ? value[start..(end + 1)] : value;
    }
}
