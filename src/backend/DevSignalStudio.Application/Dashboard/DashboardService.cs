using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Models;

namespace DevSignalStudio.Application.Dashboard;

public sealed class DashboardService
{
    private readonly IContentWorkspace _workspace;
    private readonly IConfigurationCatalog _configuration;
    private readonly IClock _clock;

    public DashboardService(
        IContentWorkspace workspace,
        IConfigurationCatalog configuration,
        IClock clock)
    {
        _workspace = workspace;
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        WorkspaceStatistics statistics = await _workspace.GetStatisticsAsync(cancellationToken);
        var runs = await _workspace.QueryIngestionRunsAsync(
            new RunQuery { Page = 1, PageSize = 1 },
            cancellationToken);
        var items = await _workspace.GetAllItemsAsync(cancellationToken);
        var sources = await _configuration.GetSourcesAsync(cancellationToken);
        var providers = await _configuration.GetAiProvidersAsync(cancellationToken);
        DateTimeOffset since = _clock.UtcNow.AddDays(-6).Date;

        Dictionary<string, int> topics = items
            .SelectMany(item => item.TopicMatches.Select(match => match.PillarName))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Take(12)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, int> activity = Enumerable.Range(0, 7)
            .Select(offset => since.AddDays(offset))
            .ToDictionary(
                date => date.ToString("yyyy-MM-dd"),
                date => items.Count(item => item.CollectedAt.Date == date.Date));

        return new DashboardSnapshot
        {
            Statistics = statistics,
            LatestIngestionRun = runs.Items.FirstOrDefault(),
            TopicDistribution = topics,
            SevenDayActivity = activity,
            EnabledSourceCount = sources.Sources.Count(source => source.Enabled),
            EnabledProviderCount = providers.Providers.Count(provider => provider.Enabled),
            GeneratedAt = _clock.UtcNow
        };
    }
}
