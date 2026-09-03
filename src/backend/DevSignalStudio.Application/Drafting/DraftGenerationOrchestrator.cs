using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Drafting;
using DevSignalStudio.Domain.Runs;

namespace DevSignalStudio.Application.Drafting;

public sealed class DraftGenerationOrchestrator
{
    private readonly IContentWorkspace _workspace;
    private readonly IConfigurationCatalog _configuration;
    private readonly IAiRouter _ai;
    private readonly IMermaidSanitizer _mermaid;
    private readonly PromptComposer _prompts;
    private readonly DraftOutputParser _parser;
    private readonly DraftValidator _validator;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public DraftGenerationOrchestrator(
        IContentWorkspace workspace,
        IConfigurationCatalog configuration,
        IAiRouter ai,
        IMermaidSanitizer mermaid,
        PromptComposer prompts,
        DraftOutputParser parser,
        DraftValidator validator,
        IClock clock,
        IIdGenerator ids)
    {
        _workspace = workspace;
        _configuration = configuration;
        _ai = ai;
        _mermaid = mermaid;
        _prompts = prompts;
        _parser = parser;
        _validator = validator;
        _clock = clock;
        _ids = ids;
    }

    public async Task ExecuteAsync(string runId, CancellationToken cancellationToken)
    {
        DraftGenerationRun run = await _workspace.GetDraftGenerationRunAsync(runId, cancellationToken)
            ?? throw new ResourceNotFoundException("Draft generation run", runId);
        if (run.Status == RunStatus.Cancelled)
        {
            return;
        }

        run = run with { Status = RunStatus.Running, StartedAt = _clock.UtcNow };
        await _workspace.SaveDraftGenerationRunAsync(run, cancellationToken);

        try
        {
            List<ContentItem> items = new();
            foreach (string itemId in run.Request.ContentItemIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ContentItem item = await _workspace.GetItemAsync(itemId, cancellationToken)
                    ?? throw new ResourceNotFoundException("Content item", itemId);
                items.Add(item);
            }

            var recipes = await _configuration.GetRecipesAsync(cancellationToken);
            var recipe = recipes.Recipes.FirstOrDefault(item =>
                item.Id.Equals(run.Request.RecipeId, StringComparison.OrdinalIgnoreCase) && item.Enabled)
                ?? throw new ResourceNotFoundException("Enabled content recipe", run.Request.RecipeId);
            var profile = await _configuration.GetProfileAsync(cancellationToken);

            DraftGenerationContext context = new()
            {
                Items = items,
                Recipe = recipe,
                Profile = profile.Profile,
                Instructions = run.Request.Instructions
            };
            AiRequest request = _prompts.Compose(context);
            RoutedAiResponse routed = await _ai.GenerateAsync(
                run.Request.ProviderRoute,
                "draft",
                request,
                cancellationToken);
            DraftAiOutput generated = _parser.Parse(routed.Response.Content);

            var mermaidResult = _mermaid.Sanitize(generated.Mermaid);
            string? safeMermaid = mermaidResult.IsValid && !string.IsNullOrWhiteSpace(mermaidResult.Sanitized)
                ? mermaidResult.Sanitized
                : null;

            HashSet<string> validItemIds = items.Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            DraftClaim[] claims = (generated.Claims ?? Array.Empty<DraftAiClaim>())
                .Where(claim => !string.IsNullOrWhiteSpace(claim.Text))
                .Select(claim =>
                {
                    string[] supportingIds = (claim.SourceIds ?? Array.Empty<string>())
                        .Where(validItemIds.Contains)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    bool needsReview = claim.NeedsReview || supportingIds.Length == 0;
                    return new DraftClaim
                    {
                        Text = claim.Text.Trim(),
                        SupportingContentItemIds = supportingIds,
                        NeedsReview = needsReview,
                        Confidence = needsReview ? "needs-review" : "source-backed"
                    };
                })
                .ToArray();

            if (claims.Length == 0)
            {
                claims = items.Take(1).Select(item => new DraftClaim
                {
                    Text = item.Summary ?? item.Title,
                    SupportingContentItemIds = new[] { item.Id },
                    Confidence = "source-backed"
                }).ToArray();
            }

            string[] hashtags = (generated.Hashtags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(NormalizeHashtag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();

            DraftContent content = new()
            {
                Title = TrimOrEmpty(generated.Title),
                Hook = TrimOrEmpty(generated.Hook),
                Body = TrimOrEmpty(generated.Body),
                Hashtags = hashtags,
                Mermaid = safeMermaid
            };
            DateTimeOffset now = _clock.UtcNow;
            Draft draft = new()
            {
                Id = _ids.NewId("draft"),
                Status = DraftStatus.InReview,
                Channel = recipe.Channel,
                RecipeId = recipe.Id,
                ContentItemIds = items.Select(item => item.Id).ToArray(),
                Revision = 1,
                Revisions = new[]
                {
                    new DraftRevision
                    {
                        Revision = 1,
                        Content = content,
                        SavedAt = now,
                        SavedBy = "ai"
                    }
                },
                References = items.Select(item => new DraftReference
                {
                    ContentItemId = item.Id,
                    Title = item.Title,
                    Url = item.Url,
                    Author = item.Author,
                    PublishedAt = item.PublishedAt
                }).ToArray(),
                Claims = claims,
                Generation = new AiGenerationMetadata
                {
                    ProviderId = routed.Response.ProviderId,
                    ProviderType = routed.Response.ProviderType,
                    Model = routed.Response.Model,
                    RouteId = routed.RouteId,
                    DurationMilliseconds = routed.Response.DurationMilliseconds,
                    InputTokens = routed.Response.InputTokens,
                    OutputTokens = routed.Response.OutputTokens,
                    FallbackErrors = routed.FallbackErrors,
                    GeneratedAt = now
                },
                CreatedAt = now,
                UpdatedAt = now
            };
            draft = draft with { Validation = _validator.Validate(draft, recipe) };

            await _workspace.SaveDraftAsync(draft, cancellationToken);
            foreach (ContentItem item in items)
            {
                await _workspace.SaveItemAsync(
                    item with { Status = ContentItemStatus.Drafted, UpdatedAt = now },
                    cancellationToken);
            }

            List<string> warnings = (routed.Response.Warnings ?? Array.Empty<string>())
                .Concat(routed.FallbackErrors.Select(error => $"AI fallback: {error}"))
                .Concat(mermaidResult.Warnings)
                .Concat(mermaidResult.Errors.Select(error => $"Diagram omitted: {error}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            run = run with
            {
                Status = warnings.Count == 0 ? RunStatus.Completed : RunStatus.CompletedWithWarnings,
                DraftId = draft.Id,
                Warnings = warnings,
                CompletedAt = _clock.UtcNow
            };
            await _workspace.SaveDraftGenerationRunAsync(run, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run = run with { Status = RunStatus.Cancelled, CompletedAt = _clock.UtcNow };
            await _workspace.SaveDraftGenerationRunAsync(run, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            run = run with
            {
                Status = RunStatus.Failed,
                Errors = new[] { exception.Message },
                CompletedAt = _clock.UtcNow
            };
            await _workspace.SaveDraftGenerationRunAsync(run, CancellationToken.None);
            throw;
        }
    }

    private static string TrimOrEmpty(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeHashtag(string value)
    {
        string tag = new(value.Trim().TrimStart('#').Where(character =>
            char.IsLetterOrDigit(character) || character == '_').ToArray());
        return string.IsNullOrWhiteSpace(tag) ? string.Empty : "#" + tag;
    }
}
