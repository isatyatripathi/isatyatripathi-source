using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Drafting;

namespace DevSignalStudio.Application.Drafting;

public sealed class DraftValidator
{
    private readonly IMermaidSanitizer _mermaid;
    private readonly IClock _clock;

    public DraftValidator(IMermaidSanitizer mermaid, IClock clock)
    {
        _mermaid = mermaid;
        _clock = clock;
    }

    public ValidationReport Validate(Draft draft, ContentRecipe recipe)
    {
        List<ValidationIssue> issues = new();
        DraftRevision? revision = draft.LatestRevision;
        if (revision is null)
        {
            issues.Add(Error("draft.no_revision", "The draft has no revision."));
            return new ValidationReport { Issues = issues, ValidatedAt = _clock.UtcNow };
        }

        DraftContent content = revision.Content;
        if (string.IsNullOrWhiteSpace(content.Hook))
        {
            issues.Add(Error("draft.hook_required", "A hook is required.", "hook"));
        }
        if (string.IsNullOrWhiteSpace(content.Body))
        {
            issues.Add(Error("draft.body_required", "A body is required.", "body"));
        }

        string plainText = BuildPlainText(content);
        if (recipe.HardMaximumCharacters is int maxCharacters && plainText.Length > maxCharacters)
        {
            issues.Add(Error(
                "draft.character_limit",
                $"The draft has {plainText.Length} characters; the maximum is {maxCharacters}.",
                "body"));
        }
        else if (recipe.TargetCharacters is int targetCharacters && plainText.Length > targetCharacters * 1.1)
        {
            issues.Add(Warning(
                "draft.above_target_length",
                $"The draft is above the {targetCharacters}-character target.",
                "body"));
        }

        int wordCount = plainText.Split(
            new[] { ' ', '\r', '\n', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Length;
        if (recipe.HardMaximumWords is int maxWords && wordCount > maxWords)
        {
            issues.Add(Error(
                "draft.word_limit",
                $"The draft has {wordCount} words; the maximum is {maxWords}.",
                "body"));
        }

        IReadOnlyList<string> hashtags = content.Hashtags ?? Array.Empty<string>();
        int hashtagCount = hashtags.Count;
        if (recipe.HashtagRange is not null &&
            (hashtagCount < recipe.HashtagRange.Min || hashtagCount > recipe.HashtagRange.Max))
        {
            issues.Add(Warning(
                "draft.hashtag_range",
                $"Use between {recipe.HashtagRange.Min} and {recipe.HashtagRange.Max} hashtags; found {hashtagCount}.",
                "hashtags"));
        }

        if (hashtags.Any(tag => string.IsNullOrWhiteSpace(tag) || !tag.StartsWith('#')))
        {
            issues.Add(Warning("draft.hashtag_format", "Every hashtag should start with #.", "hashtags"));
        }

        if (draft.References.Count == 0)
        {
            issues.Add(Error("draft.references_required", "At least one source reference is required."));
        }

        foreach (DraftClaim claim in draft.Claims ?? Array.Empty<DraftClaim>())
        {
            if (claim.SupportingContentItemIds.Count == 0)
            {
                issues.Add(Error(
                    "draft.claim_without_source",
                    $"The claim '{Truncate(claim.Text, 80)}' has no source reference.",
                    "claims"));
            }
            else if (claim.NeedsReview)
            {
                issues.Add(Warning(
                    "draft.claim_needs_review",
                    $"Review the claim '{Truncate(claim.Text, 80)}'.",
                    "claims"));
            }
        }

        if (!string.IsNullOrWhiteSpace(content.Mermaid))
        {
            var result = _mermaid.Sanitize(content.Mermaid);
            issues.AddRange(result.Errors.Select(error => Error("draft.unsafe_mermaid", error, "mermaid")));
            issues.AddRange(result.Warnings.Select(warning => Warning("draft.mermaid_warning", warning, "mermaid")));
        }

        return new ValidationReport { Issues = issues, ValidatedAt = _clock.UtcNow };
    }

    public static string BuildPlainText(DraftContent content)
    {
        string hashtags = string.Join(" ", content.Hashtags ?? Array.Empty<string>());
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            new[]
            {
                content.Hook?.Trim() ?? string.Empty,
                content.Body?.Trim() ?? string.Empty,
                hashtags.Trim()
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static ValidationIssue Error(string code, string message, string? field = null) => new()
    {
        Code = code,
        Message = message,
        Field = field,
        Severity = ValidationSeverity.Error
    };

    private static ValidationIssue Warning(string code, string message, string? field = null) => new()
    {
        Code = code,
        Message = message,
        Field = field,
        Severity = ValidationSeverity.Warning
    };

    private static string Truncate(string? value, int length)
    {
        string safeValue = value ?? string.Empty;
        return safeValue.Length <= length ? safeValue : safeValue[..length] + "...";
    }
}
