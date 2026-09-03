using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DevSignalStudio.Application.Content;

public static partial class ContentIdentity
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "ref_src"
    };

    public static string? CanonicalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        UriBuilder builder = new(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443) ||
            (builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80))
        {
            builder.Port = -1;
        }

        string path = string.IsNullOrWhiteSpace(builder.Path) ? "/" : builder.Path;
        builder.Path = path.Length > 1 ? path.TrimEnd('/') : path;

        List<KeyValuePair<string, string>> query = ParseQuery(uri.Query)
            .Where(pair => !pair.Key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            .Where(pair => !TrackingParameters.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ToList();

        builder.Query = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return builder.Uri.AbsoluteUri;
    }

    public static string CreateFingerprint(string title, string? summary, string? content)
    {
        string normalized = string.Join(
            "|",
            NormalizeForIdentity(title),
            NormalizeForIdentity(summary),
            NormalizeForIdentity(content));

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string CreateContentId(
        string sourceId,
        string externalId,
        string? canonicalUrl,
        string fingerprint)
    {
        string identity = string.Join("|", sourceId, externalId, canonicalUrl, fingerprint);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"item_{Convert.ToHexString(hash).ToLowerInvariant()[..24]}";
    }

    public static IReadOnlySet<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return WordRegex()
            .Matches(WebUtility.HtmlDecode(value).ToLowerInvariant())
            .Select(match => match.Value)
            .Where(token => token.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static double Jaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1;
        }

        int intersection = left.Count(right.Contains);
        int union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    public static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        string trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            yield break;
        }

        foreach (string segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = segment.Split('=', 2);
            string key = WebUtility.UrlDecode(parts[0]);
            string value = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                yield return new KeyValuePair<string, string>(key, value);
            }
        }
    }

    private static string NormalizeForIdentity(string? value) =>
        NonIdentityRegex().Replace(CollapseWhitespace(value).ToLowerInvariant(), " ").Trim();

    [GeneratedRegex(@"[a-z0-9][a-z0-9+#.\-]*", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9+#.\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NonIdentityRegex();
}
