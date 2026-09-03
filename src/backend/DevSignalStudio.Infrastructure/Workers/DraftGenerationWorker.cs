using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Drafting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevSignalStudio.Infrastructure.Workers;

public sealed class DraftGenerationWorker : BackgroundService
{
    private readonly IDraftGenerationQueue _queue;
    private readonly IRunCancellationRegistry _cancellations;
    private readonly DraftGenerationOrchestrator _orchestrator;
    private readonly ILogger<DraftGenerationWorker> _logger;

    public DraftGenerationWorker(
        IDraftGenerationQueue queue,
        IRunCancellationRegistry cancellations,
        DraftGenerationOrchestrator orchestrator,
        ILogger<DraftGenerationWorker> logger)
    {
        _queue = queue;
        _cancellations = cancellations;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            string runId;
            try
            {
                runId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["RunId"] = runId,
                ["RunType"] = "draft-generation"
            });
            CancellationTokenSource runCancellation = _cancellations.Register(runId, stoppingToken);
            try
            {
                _logger.LogInformation("Starting draft generation run {RunId}.", runId);
                await _orchestrator.ExecuteAsync(runId, runCancellation.Token);
                _logger.LogInformation("Finished draft generation run {RunId}.", runId);
            }
            catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
            {
                _logger.LogInformation("Draft generation run {RunId} was cancelled.", runId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Draft generation run {RunId} failed.", runId);
            }
            finally
            {
                _cancellations.Complete(runId);
            }
        }
    }
}
