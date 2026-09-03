using System.Text.Json.Serialization;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Content;

namespace DevSignalStudio.Domain.Drafting;

public sealed record DraftContent
{
    public string Title { get; init; } = string.Empty;
    public string Hook { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public IReadOnlyList<string> Hashtags { get; init; } = Array.Empty<string>();
    public string? Mermaid { get; init; }
}

public sealed record DraftRevision
{
    public int Revision { get; init; }
    public DraftContent Content { get; init; } = new();
    public DateTimeOffset SavedAt { get; init; }
    public string SavedBy { get; init; } = "system";
    public string? Note { get; init; }
}

public sealed record DraftReference
{
    public string ContentItemId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Url { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

public sealed record DraftClaim
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> SupportingContentItemIds { get; init; } = Array.Empty<string>();
    public string Confidence { get; init; } = "source-backed";
    public bool NeedsReview { get; init; }
}

public sealed record AiGenerationMetadata
{
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string RouteId { get; init; } = string.Empty;
    public long DurationMilliseconds { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public IReadOnlyList<string> FallbackErrors { get; init; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record PublishedRecord
{
    public string Channel { get; init; } = string.Empty;
    public string? Url { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
}

public sealed record Draft
{
    public string Id { get; init; } = string.Empty;
    public DraftStatus Status { get; init; } = DraftStatus.Generating;
    public string Channel { get; init; } = "linkedin";
    public string RecipeId { get; init; } = string.Empty;
    public IReadOnlyList<string> ContentItemIds { get; init; } = Array.Empty<string>();
    public int Revision { get; init; }
    public IReadOnlyList<DraftRevision> Revisions { get; init; } = Array.Empty<DraftRevision>();
    public IReadOnlyList<DraftReference> References { get; init; } = Array.Empty<DraftReference>();
    public IReadOnlyList<DraftClaim> Claims { get; init; } = Array.Empty<DraftClaim>();
    public ValidationReport Validation { get; init; } = new();
    public AiGenerationMetadata? Generation { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? RejectionReason { get; init; }
    public PublishedRecord? Publication { get; init; }

    [JsonIgnore]
    public DraftRevision? LatestRevision => Revisions.LastOrDefault();

    public Draft Edit(DraftContent content, int expectedRevision, DateTimeOffset now, string savedBy = "user")
    {
        EnsureRevision(expectedRevision);
        if (Status is DraftStatus.Published or DraftStatus.Archived)
        {
            throw new DomainRuleException("draft_not_editable", $"Draft '{Id}' cannot be edited while {Status}.");
        }

        int nextRevision = Revision + 1;
        DraftRevision revision = new()
        {
            Revision = nextRevision,
            Content = content,
            SavedAt = now,
            SavedBy = savedBy
        };

        return this with
        {
            Status = DraftStatus.InReview,
            Revision = nextRevision,
            Revisions = Revisions.Concat(new[] { revision }).ToArray(),
            Validation = new ValidationReport(),
            RejectionReason = null,
            UpdatedAt = now
        };
    }

    public Draft Approve(int expectedRevision, ValidationReport validation, DateTimeOffset now)
    {
        EnsureRevision(expectedRevision);
        if (Status != DraftStatus.InReview)
        {
            throw new DomainRuleException("draft_not_in_review", "Only a draft in review can be approved.");
        }
        if (!validation.IsValid)
        {
            throw new DomainRuleException("draft_validation_failed", "Resolve blocking validation issues before approval.");
        }

        return this with
        {
            Status = DraftStatus.Approved,
            Validation = validation,
            RejectionReason = null,
            UpdatedAt = now
        };
    }

    public Draft Reject(int expectedRevision, string reason, DateTimeOffset now)
    {
        EnsureRevision(expectedRevision);
        if (Status != DraftStatus.InReview)
        {
            throw new DomainRuleException("draft_not_in_review", "Only a draft in review can be rejected.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleException("rejection_reason_required", "A rejection reason is required.");
        }

        return this with
        {
            Status = DraftStatus.Rejected,
            RejectionReason = reason.Trim(),
            UpdatedAt = now
        };
    }

    public Draft MarkPublished(int expectedRevision, string channel, string? url, DateTimeOffset publishedAt, DateTimeOffset now)
    {
        EnsureRevision(expectedRevision);
        if (Status != DraftStatus.Approved)
        {
            throw new DomainRuleException("draft_not_approved", "Only an approved draft can be marked as published.");
        }
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new DomainRuleException("publication_channel_required", "A publication channel is required.");
        }

        string? normalizedUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (normalizedUrl is not null &&
            (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? publicationUri) ||
             (publicationUri.Scheme != Uri.UriSchemeHttp && publicationUri.Scheme != Uri.UriSchemeHttps) ||
             !string.IsNullOrEmpty(publicationUri.UserInfo)))
        {
            throw new DomainRuleException(
                "publication_url_invalid",
                "The publication URL must be an absolute HTTP or HTTPS URL without embedded credentials.");
        }

        return this with
        {
            Status = DraftStatus.Published,
            Publication = new PublishedRecord
            {
                Channel = channel.Trim(),
                Url = normalizedUrl,
                PublishedAt = publishedAt
            },
            UpdatedAt = now
        };
    }

    private void EnsureRevision(int expectedRevision)
    {
        if (Revision != expectedRevision)
        {
            throw new ConcurrencyConflictException("Draft", Id, expectedRevision, Revision);
        }
    }
}

public sealed record DraftAiOutput
{
    public string Title { get; init; } = string.Empty;
    public string Hook { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public IReadOnlyList<string> Hashtags { get; init; } = Array.Empty<string>();
    public string? Mermaid { get; init; }
    public IReadOnlyList<DraftAiClaim> Claims { get; init; } = Array.Empty<DraftAiClaim>();
}

public sealed record DraftAiClaim
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceIds { get; init; } = Array.Empty<string>();
    public bool NeedsReview { get; init; }
}

public sealed record DraftGenerationContext
{
    public IReadOnlyList<ContentItem> Items { get; init; } = Array.Empty<ContentItem>();
    public ContentRecipe Recipe { get; init; } = new();
    public AuthorProfile Profile { get; init; } = new();
    public string? Instructions { get; init; }
}
