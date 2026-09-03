using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Drafting;
using DevSignalStudio.Domain.Runs;

namespace DevSignalStudio.Application.Abstractions;

public interface IContentWorkspace
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);

    Task<PagedResult<ContentItem>> QueryItemsAsync(ContentQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContentItem>> GetAllItemsAsync(CancellationToken cancellationToken);
    Task<ContentItem?> GetItemAsync(string id, CancellationToken cancellationToken);
    Task<ContentItem?> FindDuplicateAsync(string? canonicalUrl, string fingerprint, CancellationToken cancellationToken);
    Task UpsertItemsAsync(IEnumerable<ContentItem> items, CancellationToken cancellationToken);
    Task SaveItemAsync(ContentItem item, CancellationToken cancellationToken);

    Task<PagedResult<Draft>> QueryDraftsAsync(DraftQuery query, CancellationToken cancellationToken);
    Task<Draft?> GetDraftAsync(string id, CancellationToken cancellationToken);
    Task SaveDraftAsync(Draft draft, CancellationToken cancellationToken);
    Task<bool> DraftExistsAsync(IEnumerable<string> contentItemIds, string recipeId, CancellationToken cancellationToken);

    Task<PagedResult<IngestionRun>> QueryIngestionRunsAsync(RunQuery query, CancellationToken cancellationToken);
    Task<IngestionRun?> GetIngestionRunAsync(string id, CancellationToken cancellationToken);
    Task SaveIngestionRunAsync(IngestionRun run, CancellationToken cancellationToken);

    Task<PagedResult<DraftGenerationRun>> QueryDraftGenerationRunsAsync(RunQuery query, CancellationToken cancellationToken);
    Task<DraftGenerationRun?> GetDraftGenerationRunAsync(string id, CancellationToken cancellationToken);
    Task SaveDraftGenerationRunAsync(DraftGenerationRun run, CancellationToken cancellationToken);

    Task<WorkspaceStatistics> GetStatisticsAsync(CancellationToken cancellationToken);
}
