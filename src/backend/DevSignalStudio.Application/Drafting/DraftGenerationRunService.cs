using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Runs;

namespace DevSignalStudio.Application.Drafting;

public sealed class DraftGenerationRunService
{
    private readonly IContentWorkspace _workspace;
    private readonly IDraftGenerationQueue _queue;
    private readonly IRunCancellationRegistry _cancellations;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public DraftGenerationRunService(
        IContentWorkspace workspace,
        IDraftGenerationQueue queue,
        IRunCancellationRegistry cancellations,
        IClock clock,
        IIdGenerator ids)
    {
        _workspace = workspace;
        _queue = queue;
        _cancellations = cancellations;
        _clock = clock;
        _ids = ids;
    }

    public async Task<DraftGenerationRun> StartAsync(
        DraftGenerationRequest request,
        CancellationToken cancellationToken)
    {
        string[] contentItemIds = (request.ContentItemIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (contentItemIds.Length == 0)
        {
            throw new RequestValidationException("At least one content item is required.");
        }
        if (contentItemIds.Length > 10)
        {
            throw new RequestValidationException("A draft can use at most 10 content items.");
        }
        if (string.IsNullOrWhiteSpace(request.RecipeId))
        {
            throw new RequestValidationException("recipeId is required.");
        }
        if (request.Instructions?.Length > 4_000)
        {
            throw new RequestValidationException("instructions cannot exceed 4,000 characters.");
        }

        DraftGenerationRun run = new()
        {
            Id = _ids.NewId("gen"),
            Status = RunStatus.Queued,
            Request = request with
            {
                ContentItemIds = contentItemIds,
                RecipeId = request.RecipeId.Trim(),
                ProviderRoute = string.IsNullOrWhiteSpace(request.ProviderRoute)
                    ? null
                    : request.ProviderRoute.Trim(),
                Instructions = string.IsNullOrWhiteSpace(request.Instructions)
                    ? null
                    : request.Instructions.Trim()
            },
            CreatedAt = _clock.UtcNow
        };

        await _workspace.SaveDraftGenerationRunAsync(run, cancellationToken);
        await _queue.EnqueueAsync(run.Id, cancellationToken);
        return run;
    }

    public async Task<DraftGenerationRun> CancelAsync(string id, CancellationToken cancellationToken)
    {
        DraftGenerationRun run = await _workspace.GetDraftGenerationRunAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Draft generation run", id);

        _cancellations.Cancel(id);
        if (run.Status == RunStatus.Queued)
        {
            run = run with { Status = RunStatus.Cancelled, CompletedAt = _clock.UtcNow };
            await _workspace.SaveDraftGenerationRunAsync(run, cancellationToken);
        }

        return run;
    }
}
