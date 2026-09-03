using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Drafting;

namespace DevSignalStudio.Application.Drafting;

public sealed class DraftService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly IContentWorkspace _workspace;
    private readonly IConfigurationCatalog _configuration;
    private readonly IMermaidSanitizer _mermaid;
    private readonly DraftValidator _validator;
    private readonly IClock _clock;

    public DraftService(
        IContentWorkspace workspace,
        IConfigurationCatalog configuration,
        IMermaidSanitizer mermaid,
        DraftValidator validator,
        IClock clock)
    {
        _workspace = workspace;
        _configuration = configuration;
        _mermaid = mermaid;
        _validator = validator;
        _clock = clock;
    }

    public async Task<Draft> GetRequiredAsync(string id, CancellationToken cancellationToken) =>
        await _workspace.GetDraftAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("Draft", id);

    public async Task<Draft> EditAsync(
        string id,
        DraftEditRequest request,
        CancellationToken cancellationToken)
    {
        Draft draft = await GetRequiredAsync(id, cancellationToken);
        var safeDiagram = _mermaid.Sanitize(request.Mermaid);
        if (!safeDiagram.IsValid)
        {
            throw new RequestValidationException(
                "The Mermaid diagram contains unsafe or unsupported syntax.",
                new Dictionary<string, string[]> { ["mermaid"] = safeDiagram.Errors.ToArray() });
        }

        DraftContent content = new()
        {
            Title = (request.Title ?? string.Empty).Trim(),
            Hook = (request.Hook ?? string.Empty).Trim(),
            Body = (request.Body ?? string.Empty).Trim(),
            Hashtags = (request.Hashtags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(NormalizeHashtag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray(),
            Mermaid = string.IsNullOrWhiteSpace(safeDiagram.Sanitized) ? null : safeDiagram.Sanitized
        };
        draft = draft.Edit(content, request.ExpectedRevision, _clock.UtcNow);
        var recipe = await GetRecipeAsync(draft.RecipeId, cancellationToken);
        draft = draft with { Validation = _validator.Validate(draft, recipe) };
        await _workspace.SaveDraftAsync(draft, cancellationToken);
        return draft;
    }

    public async Task<Draft> ValidateAsync(string id, CancellationToken cancellationToken)
    {
        Draft draft = await GetRequiredAsync(id, cancellationToken);
        var recipe = await GetRecipeAsync(draft.RecipeId, cancellationToken);
        draft = draft with { Validation = _validator.Validate(draft, recipe), UpdatedAt = _clock.UtcNow };
        await _workspace.SaveDraftAsync(draft, cancellationToken);
        return draft;
    }

    public async Task<Draft> ApproveAsync(
        string id,
        int expectedRevision,
        CancellationToken cancellationToken)
    {
        Draft draft = await GetRequiredAsync(id, cancellationToken);
        var recipe = await GetRecipeAsync(draft.RecipeId, cancellationToken);
        var validation = _validator.Validate(draft, recipe);
        draft = draft.Approve(expectedRevision, validation, _clock.UtcNow);
        await _workspace.SaveDraftAsync(draft, cancellationToken);
        return draft;
    }

    public async Task<Draft> RejectAsync(
        string id,
        int expectedRevision,
        string reason,
        CancellationToken cancellationToken)
    {
        Draft draft = await GetRequiredAsync(id, cancellationToken);
        draft = draft.Reject(expectedRevision, reason, _clock.UtcNow);
        await _workspace.SaveDraftAsync(draft, cancellationToken);
        return draft;
    }

    public async Task<Draft> MarkPublishedAsync(
        string id,
        MarkPublishedRequest request,
        CancellationToken cancellationToken)
    {
        Draft draft = await GetRequiredAsync(id, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Channel))
        {
            throw new RequestValidationException("channel is required.");
        }
        if (!string.IsNullOrWhiteSpace(request.Url) &&
            (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? publicationUri) ||
             (publicationUri.Scheme != Uri.UriSchemeHttp && publicationUri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new RequestValidationException("url must be an absolute HTTP or HTTPS URL.");
        }

        draft = draft.MarkPublished(
            request.ExpectedRevision,
            request.Channel,
            string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim(),
            request.PublishedAt ?? _clock.UtcNow,
            _clock.UtcNow);
        await _workspace.SaveDraftAsync(draft, cancellationToken);
        return draft;
    }

    public async Task<ExportArtifact> ExportAsync(
        string id,
        string format,
        CancellationToken cancellationToken)
    {
        Draft draft = await GetRequiredAsync(id, cancellationToken);
        DraftRevision revision = draft.LatestRevision
            ?? throw new DomainRuleException("draft.no_revision", "The draft has no revision.");
        string normalized = string.IsNullOrWhiteSpace(format)
            ? "plain"
            : format.Trim().ToLowerInvariant();

        return normalized switch
        {
            "plain" => new ExportArtifact(
                $"{draft.Id}.txt",
                "text/plain; charset=utf-8",
                DraftValidator.BuildPlainText(revision.Content)),
            "markdown" => new ExportArtifact(
                $"{draft.Id}.md",
                "text/markdown; charset=utf-8",
                BuildMarkdown(draft, revision.Content)),
            "json" => new ExportArtifact(
                $"{draft.Id}.json",
                "application/json; charset=utf-8",
                JsonSerializer.Serialize(draft, JsonOptions)),
            "mermaid" => new ExportArtifact(
                $"{draft.Id}.mmd",
                "text/plain; charset=utf-8",
                revision.Content.Mermaid ?? string.Empty),
            _ => throw new RequestValidationException(
                "format must be one of: plain, markdown, json, mermaid.")
        };
    }

    private async Task<DevSignalStudio.Domain.Configuration.ContentRecipe> GetRecipeAsync(
        string recipeId,
        CancellationToken cancellationToken)
    {
        var recipes = await _configuration.GetRecipesAsync(cancellationToken);
        return recipes.Recipes.FirstOrDefault(recipe =>
            recipe.Id.Equals(recipeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ResourceNotFoundException("Content recipe", recipeId);
    }

    private static string NormalizeHashtag(string value)
    {
        string tag = new(value.Trim().TrimStart('#').Where(character =>
            char.IsLetterOrDigit(character) || character == '_').ToArray());
        return string.IsNullOrWhiteSpace(tag) ? string.Empty : "#" + tag;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string BuildMarkdown(Draft draft, DraftContent content)
    {
        StringBuilder builder = new();
        if (!string.IsNullOrWhiteSpace(content.Title))
        {
            builder.AppendLine($"# {content.Title}").AppendLine();
        }
        builder.AppendLine(content.Hook).AppendLine();
        builder.AppendLine(content.Body).AppendLine();
        if (content.Hashtags.Count > 0)
        {
            builder.AppendLine(string.Join(" ", content.Hashtags)).AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(content.Mermaid))
        {
            builder.AppendLine("```mermaid");
            builder.AppendLine(content.Mermaid);
            builder.AppendLine("```").AppendLine();
        }
        builder.AppendLine("## Sources");
        foreach (DraftReference reference in draft.References)
        {
            builder.AppendLine(reference.Url is null
                ? $"- {reference.Title}"
                : $"- [{reference.Title}]({reference.Url})");
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }
}
