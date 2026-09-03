using System.Diagnostics;
using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;
using DevSignalStudio.Infrastructure.Common;
using DevSignalStudio.Infrastructure.Security;

namespace DevSignalStudio.Infrastructure.Sources;

public sealed class HttpJsonConnector : IContentConnector
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Create();
    private readonly SafeHttpFetcher _http;
    private readonly IClock _clock;

    public HttpJsonConnector(SafeHttpFetcher http, IClock clock)
    {
        _http = http;
        _clock = clock;
    }

    public string ConnectorType => "http-json";

    public async Task<ConnectorHealth> CheckHealthAsync(
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            _ = await FetchAsync(source with { MaxItemsPerRun = 1 }, cancellationToken);
            return new ConnectorHealth
            {
                Status = HealthState.Healthy,
                Message = "The JSON endpoint returned a compatible document.",
                CheckedAt = _clock.UtcNow,
                Latency = stopwatch.Elapsed
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ConnectorHealth
            {
                Status = HealthState.Unhealthy,
                Message = exception.Message,
                CheckedAt = _clock.UtcNow,
                Latency = stopwatch.Elapsed
            };
        }
    }

    public async Task<ConnectorFetchResult> FetchAsync(
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.Endpoint))
        {
            throw new InvalidOperationException("An HTTP JSON endpoint is required.");
        }

        string json = await _http.GetStringAsync(source.Endpoint, cancellationToken);
        CuratedItemsDocument? document = JsonSerializer.Deserialize<CuratedItemsDocument>(json, JsonOptions);
        if (document is null)
        {
            throw new InvalidDataException("The endpoint returned an empty JSON document.");
        }

        return new ConnectorFetchResult
        {
            Items = CuratedItemMapper.Map(document)
                .Take(Math.Clamp(source.MaxItemsPerRun ?? 100, 1, 500))
                .ToArray()
        };
    }
}
