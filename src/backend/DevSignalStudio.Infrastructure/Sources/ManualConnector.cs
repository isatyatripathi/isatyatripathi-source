using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Infrastructure.Sources;

public sealed class ManualConnector : IContentConnector
{
    private readonly IClock _clock;

    public ManualConnector(IClock clock)
    {
        _clock = clock;
    }

    public string ConnectorType => "manual";

    public Task<ConnectorHealth> CheckHealthAsync(
        SourceDefinition source,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectorHealth
        {
            Status = HealthState.Healthy,
            Message = "Manual capture is ready; it does not fetch remote content.",
            CheckedAt = _clock.UtcNow
        });

    public Task<ConnectorFetchResult> FetchAsync(
        SourceDefinition source,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectorFetchResult
        {
            Warnings = new[] { "Manual sources are populated through POST /api/v1/items/manual." }
        });
}
