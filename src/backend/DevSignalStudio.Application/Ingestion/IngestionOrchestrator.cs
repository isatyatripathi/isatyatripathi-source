using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Content;
using DevSignalStudio.Application.Drafting;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Runs;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Ingestion;

public sealed class IngestionOrchestrator
{
    private readonly IConfigurationCatalog _configuration;
    private readonly IContentWorkspace _workspace;
    private readonly IConnectorRegistry _connectors;
    private readonly ContentNormalizer _normalizer;
    private readonly IRelevanceScorer _scorer;
    private readonly DraftGenerationRunService _draftRuns;
    private readonly IClock _clock;

    public IngestionOrchestrator(
        IConfigurationCatalog configuration,
        IContentWorkspace workspace,
        IConnectorRegistry connectors,
        ContentNormalizer normalizer,
        IRelevanceScorer scorer,
        DraftGenerationRunService draftRuns,
        IClock clock)
    {
        _configuration = configuration;
        _workspace = workspace;
        _connectors = connectors;
        _normalizer = normalizer;
        _scorer = scorer;
        _draftRuns = draftRuns;
        _clock = clock;
    }

    public async Task ExecuteAsync(string runId, CancellationToken cancellationToken)
    {
        IngestionRun run = await _workspace.GetIngestionRunAsync(runId, cancellationToken)
            ?? throw new ResourceNotFoundException("Ingestion run", runId);

        if (run.Status == RunStatus.Cancelled)
        {
            return;
        }

        run = run with { Status = RunStatus.Running, StartedAt = _clock.UtcNow };
        await _workspace.SaveIngestionRunAsync(run, cancellationToken);

        List<SourceFetchSummary> summaries = new();
        List<string> runWarnings = new();
        List<string> runErrors = new();
        List<ContentItem> pending = new();
        int fetched = 0;
        int duplicates = 0;

        try
        {
            SourcesDocument sourceDocument = await _configuration.GetSourcesAsync(cancellationToken);
            var taxonomy = await _configuration.GetTopicsAsync(cancellationToken);
            IReadOnlyList<ContentItem> existingItems = await _workspace.GetAllItemsAsync(cancellationToken);
            List<ContentItem> scoringContext = existingItems.ToList();

            HashSet<string> knownFingerprints = existingItems
                .Select(item => item.ContentFingerprint)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> knownUrls = existingItems
                .Select(item => item.CanonicalUrl)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<string> requested = (run.Request.SourceIds ?? Array.Empty<string>())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            SourceDefinition[] configuredSources = (sourceDocument.Sources ?? Array.Empty<SourceDefinition>())
                .ToArray();
            SourceDefinition[] sources = configuredSources
                .Where(source => source.Enabled)
                .Where(source => !source.ConnectorType.Equals("manual", StringComparison.OrdinalIgnoreCase))
                .Where(source => requested.Count == 0 || requested.Contains(source.Id))
                .ToArray();

            string[] requestedManualSources = requested
                .Where(id => configuredSources.Any(source =>
                    source.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                    source.ConnectorType.Equals("manual", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (requestedManualSources.Length > 0)
            {
                runWarnings.Add(
                    $"Manual sources are not polled and were skipped: {string.Join(", ", requestedManualSources)}. " +
                    "Use POST /api/v1/items/manual to capture them.");
            }

            if (!run.Request.Force &&
                !run.Trigger.Equals("schedule", StringComparison.OrdinalIgnoreCase) &&
                sources.Any(source => source.PollMinutes is > 0))
            {
                PagedResult<IngestionRun> history = await _workspace.QueryIngestionRunsAsync(
                    new RunQuery { Page = 1, PageSize = 200 },
                    cancellationToken);
                Dictionary<string, DateTimeOffset> lastSuccessfulFetch = history.Items
                    .Where(previous => !previous.Id.Equals(run.Id, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(previous => previous.Sources)
                    .Where(summary =>
                        summary.CompletedAt.HasValue &&
                        summary.Status is RunStatus.Completed or RunStatus.CompletedWithWarnings)
                    .GroupBy(summary => summary.SourceId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Max(summary => summary.CompletedAt!.Value),
                        StringComparer.OrdinalIgnoreCase);

                List<SourceDefinition> dueSources = new();
                foreach (SourceDefinition source in sources)
                {
                    if (source.PollMinutes is not int pollMinutes ||
                        !lastSuccessfulFetch.TryGetValue(source.Id, out DateTimeOffset lastFetch) ||
                        _clock.UtcNow >= lastFetch.AddMinutes(pollMinutes))
                    {
                        dueSources.Add(source);
                        continue;
                    }

                    runWarnings.Add(
                        $"{source.Name} was skipped because its {pollMinutes}-minute poll interval has not elapsed. " +
                        "Set force=true to override it.");
                }
                sources = dueSources.ToArray();
            }

            if (requested.Count > 0)
            {
                string[] missing = requested
                    .Where(id => configuredSources.All(source =>
                        !source.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (missing.Length > 0)
                {
                    runWarnings.Add($"Unknown source IDs were ignored: {string.Join(", ", missing)}.");
                }

                string[] disabled = requested
                    .Where(id => (sourceDocument.Sources ?? Array.Empty<SourceDefinition>()).Any(source =>
                        source.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && !source.Enabled))
                    .ToArray();
                if (disabled.Length > 0)
                {
                    runWarnings.Add($"Disabled source IDs were ignored: {string.Join(", ", disabled)}.");
                }
            }

            if (sources.Length == 0)
            {
                runWarnings.Add("No enabled sources matched this run.");
            }

            foreach (SourceDefinition source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTimeOffset sourceStarted = _clock.UtcNow;
                List<string> sourceWarnings = new();
                List<string> sourceErrors = new();
                int sourceFetched = 0;
                int sourceAdded = 0;
                int sourceDuplicates = 0;
                int sourceCandidates = 0;

                try
                {
                    IContentConnector connector = _connectors.GetRequired(source.ConnectorType);
                    ConnectorFetchResult result = await connector.FetchAsync(source, cancellationToken);
                    sourceWarnings.AddRange(result.Warnings);

                    int maximum = Math.Clamp(source.MaxItemsPerRun ?? 20, 1, 500);
                    foreach (RawContentItem raw in result.Items.Take(maximum))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sourceFetched++;
                        fetched++;

                        if (string.IsNullOrWhiteSpace(raw.Title))
                        {
                            sourceWarnings.Add("One item was skipped because it had no title.");
                            continue;
                        }

                        ContentItem item = _normalizer.Normalize(raw, source);
                        bool duplicate = knownFingerprints.Contains(item.ContentFingerprint) ||
                            (item.CanonicalUrl is not null && knownUrls.Contains(item.CanonicalUrl));
                        if (duplicate)
                        {
                            sourceDuplicates++;
                            duplicates++;
                            continue;
                        }

                        var (score, matches) = _scorer.Score(item, source, taxonomy, scoringContext);
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

                        pending.Add(item);
                        scoringContext.Add(item);
                        knownFingerprints.Add(item.ContentFingerprint);
                        if (item.CanonicalUrl is not null)
                        {
                            knownUrls.Add(item.CanonicalUrl);
                        }

                        sourceAdded++;
                        if (status == ContentItemStatus.Candidate)
                        {
                            sourceCandidates++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    sourceErrors.Add(exception.Message);
                    runErrors.Add($"{source.Name}: {exception.Message}");
                }

                SourceFetchSummary summary = new()
                {
                    SourceId = source.Id,
                    SourceName = source.Name,
                    Status = sourceErrors.Count == 0
                        ? (sourceWarnings.Count == 0 ? RunStatus.Completed : RunStatus.CompletedWithWarnings)
                        : RunStatus.Failed,
                    Fetched = sourceFetched,
                    Added = sourceAdded,
                    Duplicates = sourceDuplicates,
                    Candidates = sourceCandidates,
                    Warnings = sourceWarnings.Distinct().ToArray(),
                    Errors = sourceErrors.Distinct().ToArray(),
                    StartedAt = sourceStarted,
                    CompletedAt = _clock.UtcNow
                };
                summaries.Add(summary);
                runWarnings.AddRange(sourceWarnings.Select(warning => $"{source.Name}: {warning}"));

                run = run with
                {
                    Fetched = fetched,
                    Added = pending.Count,
                    Duplicates = duplicates,
                    Candidates = pending.Count(item => item.Status == ContentItemStatus.Candidate),
                    Sources = summaries.ToArray(),
                    Warnings = runWarnings.Distinct().ToArray(),
                    Errors = runErrors.Distinct().ToArray()
                };
                await _workspace.SaveIngestionRunAsync(run, cancellationToken);
            }

            int candidateLimit = Math.Min(
                Math.Clamp(run.Request.MaxCandidates, 1, 200),
                Math.Max(1, taxonomy.Profile.DailyCandidateLimit));
            HashSet<string> selectedCandidateIds = pending
                .Where(item => item.Status == ContentItemStatus.Candidate)
                .OrderByDescending(item => item.Score.FinalScore)
                .Take(candidateLimit)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            pending = pending
                .Select(item => item with
                {
                    Status = selectedCandidateIds.Contains(item.Id)
                        ? ContentItemStatus.Candidate
                        : ContentItemStatus.Collected
                })
                .ToList();

            if (pending.Count > 0)
            {
                await _workspace.UpsertItemsAsync(pending, cancellationToken);
            }

            int draftsQueued = 0;
            if (run.Request.GenerateDrafts)
            {
                ContentItem[] draftCandidates = pending
                    .Where(item => selectedCandidateIds.Contains(item.Id))
                    .OrderByDescending(item => item.Score.FinalScore)
                    .Take(Math.Max(1, taxonomy.Profile.DraftCandidateLimit))
                    .ToArray();

                foreach (ContentItem item in draftCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string recipeId = SelectRecipe(item);
                    if (await _workspace.DraftExistsAsync(
                        new[] { item.Id },
                        recipeId,
                        cancellationToken))
                    {
                        continue;
                    }

                    await _draftRuns.StartAsync(
                        new DraftGenerationRequest
                        {
                            ContentItemIds = new[] { item.Id },
                            RecipeId = recipeId
                        },
                        cancellationToken);
                    draftsQueued++;
                }
            }

            bool anySuccessfulSource = summaries.Any(summary => summary.Status is
                RunStatus.Completed or RunStatus.CompletedWithWarnings);
            RunStatus finalStatus = runErrors.Count switch
            {
                > 0 when !anySuccessfulSource => RunStatus.Failed,
                > 0 => RunStatus.CompletedWithWarnings,
                _ when runWarnings.Count > 0 => RunStatus.CompletedWithWarnings,
                _ => RunStatus.Completed
            };

            run = run with
            {
                Status = finalStatus,
                CompletedAt = _clock.UtcNow,
                Fetched = fetched,
                Added = pending.Count,
                Duplicates = duplicates,
                Candidates = selectedCandidateIds.Count,
                DraftsQueued = draftsQueued,
                Sources = summaries.ToArray(),
                Warnings = runWarnings.Distinct().ToArray(),
                Errors = runErrors.Distinct().ToArray()
            };
            await _workspace.SaveIngestionRunAsync(run, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run = run with
            {
                Status = RunStatus.Cancelled,
                CompletedAt = _clock.UtcNow,
                Fetched = fetched,
                Added = pending.Count,
                Duplicates = duplicates,
                Sources = summaries.ToArray(),
                Warnings = runWarnings.Distinct().ToArray(),
                Errors = runErrors.Distinct().ToArray()
            };
            await _workspace.SaveIngestionRunAsync(run, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            runErrors.Add(exception.Message);
            run = run with
            {
                Status = RunStatus.Failed,
                CompletedAt = _clock.UtcNow,
                Fetched = fetched,
                Added = pending.Count,
                Duplicates = duplicates,
                Sources = summaries.ToArray(),
                Warnings = runWarnings.Distinct().ToArray(),
                Errors = runErrors.Distinct().ToArray()
            };
            await _workspace.SaveIngestionRunAsync(run, CancellationToken.None);
            throw;
        }
    }

    private static string SelectRecipe(ContentItem item)
    {
        HashSet<string> pillars = item.TopicMatches
            .Select(match => match.PillarId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (pillars.Contains("system-design"))
        {
            return "linkedin-system-design";
        }
        if (pillars.Contains("interviews-dsa"))
        {
            return "linkedin-interview-question";
        }
        if (pillars.Contains("leadership-career"))
        {
            return "linkedin-leadership-lesson";
        }

        return "linkedin-explainer";
    }
}
