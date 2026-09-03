using System.Text.RegularExpressions;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Models;

namespace DevSignalStudio.Infrastructure.Security;

public sealed partial class MermaidSanitizer : IMermaidSanitizer
{
    private const int MaximumLength = 20_000;
    private static readonly string[] AllowedStarts =
    {
        "flowchart", "graph", "sequenceDiagram", "classDiagram", "stateDiagram",
        "stateDiagram-v2", "erDiagram", "journey", "gantt", "pie", "mindmap",
        "timeline", "quadrantChart", "xychart-beta"
    };
    private static readonly string[] ForbiddenTokens =
    {
        "%%{", "click ", "javascript:", "<script", "</script", "<iframe", "onload=",
        "onerror=", "href=", "xlink:href", "data:text/html", "foreignObject"
    };

    public MermaidSanitizationResult Sanitize(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new MermaidSanitizationResult { IsValid = true };
        }

        string value = StripFences(source).Replace("\r\n", "\n").Trim();
        List<string> errors = new();
        List<string> warnings = new();

        if (value.Length > MaximumLength)
        {
            errors.Add($"Mermaid source exceeds the {MaximumLength}-character limit.");
        }
        if (ForbiddenTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Mermaid source contains scripts, links, click handlers, HTML, or init directives.");
        }
        if (ControlCharacterRegex().IsMatch(value))
        {
            errors.Add("Mermaid source contains unsupported control characters.");
        }

        string first = value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !line.StartsWith("%%", StringComparison.Ordinal)) ?? string.Empty;
        if (!AllowedStarts.Any(start => first.StartsWith(start, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("The Mermaid diagram type is not on the allow list.");
        }

        if (value.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("External URLs are not allowed in Mermaid source.");
        }

        if (value.Split('\n').Length > 250)
        {
            warnings.Add("The diagram is large and may be difficult to read in a social post.");
        }

        return new MermaidSanitizationResult
        {
            IsValid = errors.Count == 0,
            Sanitized = errors.Count == 0 ? value : string.Empty,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static string StripFences(string value)
    {
        string trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstLineEnd = trimmed.IndexOf('\n');
        int closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && closing > firstLineEnd
            ? trimmed[(firstLineEnd + 1)..closing].Trim()
            : trimmed;
    }

    [GeneratedRegex("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F]", RegexOptions.Compiled)]
    private static partial Regex ControlCharacterRegex();
}
