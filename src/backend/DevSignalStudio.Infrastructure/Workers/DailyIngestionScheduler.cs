using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Ingestion;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Runs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevSignalStudio.Infrastructure.Workers;

/// <summary>
/// Lightweight local scheduler. It intentionally avoids a persistent scheduler
/// dependency: the JSON run history is enough to decide whether today's run has
/// already been queued.
/// </summary>
public sealed class DailyIngestionScheduler : BackgroundService
{
    private readonly IConfigurationCatalog _configuration;
    private readonly IContentWorkspace _workspace;
    private readonly IngestionRunService _runs;
    private readonly IClock _clock;
    private readonly ILogger<DailyIngestionScheduler> _logger;
    private bool _startupPolicyEvaluated;
    private DateOnly? _skipOverdueDate;

    public DailyIngestionScheduler(
        IConfigurationCatalog configuration,
        IContentWorkspace workspace,
        IngestionRunService runs,
        IClock clock,
        ILogger<DailyIngestionScheduler> logger)
    {
        _configuration = configuration;
        _workspace = workspace;
        _runs = runs;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup initialization and the API complete first.
        await DelaySafelyAsync(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The daily ingestion scheduler check failed.");
            }

            await DelaySafelyAsync(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        ProfileSettingsDocument profile = await _configuration.GetProfileAsync(cancellationToken);
        ScheduleSettings schedule = profile.Schedule;
        if (!schedule.Enabled || schedule.MaximumRunsPerDay <= 0)
        {
            return;
        }
        if (!TimeOnly.TryParse(schedule.LocalTime, out TimeOnly scheduledTime))
        {
            _logger.LogWarning("Daily ingestion time '{ConfiguredTime}' is invalid.", schedule.LocalTime);
            return;
        }

        DateTimeOffset now = _clock.UtcNow.ToLocalTime();
        DateOnly localDate = DateOnly.FromDateTime(now.DateTime);
        DateTime scheduledLocal = localDate.ToDateTime(scheduledTime);
        bool due = now.DateTime >= scheduledLocal;

        if (!_startupPolicyEvaluated)
        {
            _startupPolicyEvaluated = true;
            if (due && !schedule.RunOnStartupWhenOverdue)
            {
                _skipOverdueDate = localDate;
                _logger.LogInformation(
                    "Skipping the overdue scheduled run for {LocalDate}; runOnStartupWhenOverdue is disabled.",
                    localDate);
            }
        }

        if (_skipOverdueDate is DateOnly skippedDate)
        {
            if (skippedDate == localDate)
            {
                return;
            }
            _skipOverdueDate = null;
        }

        if (!due)
        {
            return;
        }

        PagedResult<IngestionRun> recent = await _workspace.QueryIngestionRunsAsync(
            new RunQuery { Page = 1, PageSize = 200 },
            cancellationToken);
        int runsToday = recent.Items.Count(run =>
            run.Trigger.Equals("schedule", StringComparison.OrdinalIgnoreCase) &&
            DateOnly.FromDateTime(run.CreatedAt.ToLocalTime().DateTime) == localDate &&
            run.Status != RunStatus.Cancelled);
        if (runsToday >= schedule.MaximumRunsPerDay)
        {
            return;
        }

        TopicTaxonomyDocument topics = await _configuration.GetTopicsAsync(cancellationToken);
        IngestionRun queued = await _runs.StartAsync(
            new IngestionRunRequest
            {
                GenerateDrafts = schedule.GenerateDrafts,
                MaxCandidates = Math.Max(1, topics.Profile.DailyCandidateLimit)
            },
            "schedule",
            cancellationToken);
        _logger.LogInformation(
            "Queued scheduled ingestion run {RunId}; automatic drafts: {GenerateDrafts}.",
            queued.Id,
            schedule.GenerateDrafts);
    }

    private static async Task DelaySafelyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
