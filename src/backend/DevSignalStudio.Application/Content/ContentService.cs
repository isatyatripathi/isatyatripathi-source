using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Content;

public sealed class ContentService
{
    private readonly IConfigurationCatalog _configuration;
    private readonly IContentWorkspace _workspace;
    private readonly ContentNormalizer _normalizer;
    private readonly IRelevanceScorer _scorer;
    private readonly IClock _clock;

    public ContentService(
        IConfigurationCatalog configuration,
        IContentWorkspace workspace,
        ContentNormalizer normalizer,
        IRelevanceScorer scorer,
        IClock clock)
    {
        _configuration = configuration;
        _workspace = workspace;
        _normalizer = normalizer;
        _scorer = scorer;
        _clock = clock;
    }

    public async Task<ContentItem> AddManualAsync(
        ManualContentRequest request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> errors = new();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = new[] { "Title is required." };
        }
        else if (request.Title.Length > 500)
        {
            errors["title"] = new[] { "Title cannot exceed 500 characters." };
        }
        if (request.Summary?.Length > 8_000)
        {
            errors["summary"] = new[] { "Summary cannot exceed 8,000 characters." };
        }
        if (request.Content?.Length > 100_000)
        {
            errors["content"] = new[] { "Content cannot exceed 100,000 characters." };
        }
        if (!string.IsNullOrWhiteSpace(request.Url) &&
            (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out Uri? itemUri) ||
             (itemUri.Scheme != Uri.UriSchemeHttp && itemUri.Scheme != Uri.UriSchemeHttps) ||
             !string.IsNullOrEmpty(itemUri.UserInfo)))
        {
            errors["url"] = new[]
            {
                "URL must be an absolute HTTP or HTTPS URL without embedded credentials."
            };
        }
        if (errors.Count > 0)
        {
            throw new RequestValidationException("The manual content item is invalid.", errors);
        }

        string sourceId = string.IsNullOrWhiteSpace(request.SourceId)
            ? "manual-local"
            : request.SourceId.Trim();
        SourceDefinition source = await _configuration.GetSourceAsync(sourceId, cancellationToken)
            ?? throw new ResourceNotFoundException("Manual source", sourceId);
        if (!source.ConnectorType.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleException(
                "source_not_manual",
                $"Source '{source.Id}' is configured as '{source.ConnectorType}', not as a manual source.");
        }

        string? normalizedUrl = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        RawContentItem raw = new()
        {
            ExternalId = normalizedUrl ?? $"manual-{Guid.NewGuid():N}",
            Title = request.Title,
            Url = normalizedUrl,
            Summary = request.Summary,
            Content = request.Content,
            Author = request.Author,
            PublishedAt = request.PublishedAt,
            Tags = request.Tags ?? Array.Empty<string>(),
            Notes = request.Notes
        };

        ContentItem item = _normalizer.Normalize(raw, source);
        ContentItem? duplicate = await _workspace.FindDuplicateAsync(
            item.CanonicalUrl,
            item.ContentFingerprint,
            cancellationToken);

        if (duplicate is not null)
        {
            throw new DomainRuleException(
                "duplicate_content",
                $"This content already exists as item '{duplicate.Id}'.");
        }

        var taxonomy = await _configuration.GetTopicsAsync(cancellationToken);
        IReadOnlyList<ContentItem> existing = await _workspace.GetAllItemsAsync(cancellationToken);
        var (score, matches) = _scorer.Score(item, source, taxonomy, existing);
        ContentItemStatus status = score.FinalScore >= taxonomy.Profile.DefaultMinimumScore
            ? ContentItemStatus.Candidate
            : ContentItemStatus.Collected;

        item = item with
        {
            Score = score,
            TopicMatches = matches,
            Status = status,
            UpdatedAt = _clock.UtcNow
        };

        await _workspace.SaveItemAsync(item, cancellationToken);
        return item;
    }

    public async Task<ContentItem> PromoteAsync(string id, CancellationToken cancellationToken)
    {
        ContentItem item = await GetRequiredAsync(id, cancellationToken);
        item = item with { Status = ContentItemStatus.Promoted, UpdatedAt = _clock.UtcNow };
        await _workspace.SaveItemAsync(item, cancellationToken);
        return item;
    }

    public async Task<ContentItem> ArchiveAsync(string id, CancellationToken cancellationToken)
    {
        ContentItem item = await GetRequiredAsync(id, cancellationToken);
        item = item with { Status = ContentItemStatus.Archived, UpdatedAt = _clock.UtcNow };
        await _workspace.SaveItemAsync(item, cancellationToken);
        return item;
    }

    public async Task<ContentItem> GetRequiredAsync(string id, CancellationToken cancellationToken) =>
        await _workspace.GetItemAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("Content item", id);
}
