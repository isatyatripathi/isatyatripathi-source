using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Ingestion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevSignalStudio.Infrastructure.Workers;

public sealed class IngestionWorker : BackgroundService
{
    private readonly IIngestionRunQueue _queue;
    private readonly IRunCancellationRegistry _cancellations;
    private readonly IngestionOrchestrator _orchestrator;
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(
        IIngestionRunQueue queue,
        IRunCancellationRegistry cancellations,
        IngestionOrchestrator orchestrator,
        ILogger<IngestionWorker> logger)
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
                ["RunType"] = "ingestion"
            });
            CancellationTokenSource runCancellation = _cancellations.Register(runId, stoppingToken);
            try
            {
                _logger.LogInformation("Starting ingestion run {RunId}.", runId);
                await _orchestrator.ExecuteAsync(runId, runCancellation.Token);
                _logger.LogInformation("Finished ingestion run {RunId}.", runId);
            }
            catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
            {
                _logger.LogInformation("Ingestion run {RunId} was cancelled.", runId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Ingestion run {RunId} failed.", runId);
            }
            finally
            {
                _cancellations.Complete(runId);
            }
        }
    }
}
