using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Runs;

namespace DevSignalStudio.Application.Ingestion;

public sealed class IngestionRunService
{
    private readonly IContentWorkspace _workspace;
    private readonly IIngestionRunQueue _queue;
    private readonly IRunCancellationRegistry _cancellations;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public IngestionRunService(
        IContentWorkspace workspace,
        IIngestionRunQueue queue,
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

    public async Task<IngestionRun> StartAsync(
        IngestionRunRequest request,
        string trigger,
        CancellationToken cancellationToken)
    {
        if (request.MaxCandidates is < 1 or > 200)
        {
            throw new RequestValidationException("maxCandidates must be between 1 and 200.");
        }

        string[] sourceIds = (request.SourceIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        IngestionRun run = new()
        {
            Id = _ids.NewId("ing"),
            Status = RunStatus.Queued,
            Trigger = string.IsNullOrWhiteSpace(trigger) ? "manual" : trigger.Trim(),
            Request = request with { SourceIds = sourceIds },
            CreatedAt = _clock.UtcNow
        };

        await _workspace.SaveIngestionRunAsync(run, cancellationToken);
        await _queue.EnqueueAsync(run.Id, cancellationToken);
        return run;
    }

    public async Task<IngestionRun> CancelAsync(string id, CancellationToken cancellationToken)
    {
        IngestionRun run = await _workspace.GetIngestionRunAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Ingestion run", id);

        _cancellations.Cancel(id);
        if (run.Status == RunStatus.Queued)
        {
            run = run with { Status = RunStatus.Cancelled, CompletedAt = _clock.UtcNow };
            await _workspace.SaveIngestionRunAsync(run, cancellationToken);
        }

        return run;
    }
}
