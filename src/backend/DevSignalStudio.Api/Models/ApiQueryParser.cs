using System.Globalization;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;

namespace DevSignalStudio.Api.Models;

internal static class ApiQueryParser
{
    public static ContentQuery ParseContent(HttpRequest request) => new()
    {
        Query = Value(request, "query"),
        Topic = Value(request, "topic"),
        SourceId = Value(request, "sourceId"),
        MinimumScore = NullableDouble(request, "minScore"),
        Status = NullableEnum<ContentItemStatus>(request, "status"),
        From = NullableDateTimeOffset(request, "from"),
        To = NullableDateTimeOffset(request, "to"),
        Sort = Value(request, "sort") ?? "score-desc",
        Page = Int(request, "page", 1, 1, int.MaxValue),
        PageSize = Int(request, "pageSize", 25, 1, 200)
    };

    public static DraftQuery ParseDraft(HttpRequest request) => new()
    {
        Status = NullableEnum<DraftStatus>(request, "status"),
        Channel = Value(request, "channel"),
        Topic = Value(request, "topic"),
        RecipeId = Value(request, "recipeId"),
        Page = Int(request, "page", 1, 1, int.MaxValue),
        PageSize = Int(request, "pageSize", 25, 1, 200)
    };

    public static RunQuery ParseRun(HttpRequest request) => new()
    {
        Status = NullableEnum<RunStatus>(request, "status"),
        Page = Int(request, "page", 1, 1, int.MaxValue),
        PageSize = Int(request, "pageSize", 25, 1, 200)
    };

    public static bool Bool(HttpRequest request, string name, bool fallback = false)
    {
        string? raw = Value(request, name);
        if (raw is null)
        {
            return fallback;
        }
        if (!bool.TryParse(raw, out bool value))
        {
            throw FieldError(name, "Use true or false.");
        }
        return value;
    }

    private static string? Value(HttpRequest request, string name)
    {
        string? value = request.Query[name].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int Int(HttpRequest request, string name, int fallback, int minimum, int maximum)
    {
        string? raw = Value(request, name);
        if (raw is null)
        {
            return fallback;
        }
        if (!int.TryParse(raw, out int value) || value < minimum || value > maximum)
        {
            throw FieldError(name, $"Use an integer between {minimum} and {maximum}.");
        }
        return value;
    }

    private static double? NullableDouble(HttpRequest request, string name)
    {
        string? raw = Value(request, name);
        if (raw is null)
        {
            return null;
        }
        if (!double.TryParse(
                raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double value))
        {
            throw FieldError(name, "Use a valid number.");
        }
        return value;
    }

    private static DateTimeOffset? NullableDateTimeOffset(HttpRequest request, string name)
    {
        string? raw = Value(request, name);
        if (raw is null)
        {
            return null;
        }
        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTimeOffset value))
        {
            throw FieldError(name, "Use an ISO-8601 date or timestamp.");
        }
        return value;
    }

    private static TEnum? NullableEnum<TEnum>(HttpRequest request, string name)
        where TEnum : struct, Enum
    {
        string? raw = Value(request, name);
        if (raw is null)
        {
            return null;
        }
        if (!Enum.TryParse(raw, ignoreCase: true, out TEnum value))
        {
            throw FieldError(name, $"Unsupported value '{raw}'.");
        }
        return value;
    }

    private static RequestValidationException FieldError(string field, string message) =>
        new("One or more query parameters are invalid.",
            new Dictionary<string, string[]> { [field] = new[] { message } });
}
