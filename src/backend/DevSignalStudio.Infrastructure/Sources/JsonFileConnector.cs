using System.Diagnostics;
using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;
using DevSignalStudio.Infrastructure.Common;

namespace DevSignalStudio.Infrastructure.Sources;

public sealed class JsonFileConnector : IContentConnector
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Create();
    private readonly IConfigurationCatalog _configuration;
    private readonly IClock _clock;

    public JsonFileConnector(IConfigurationCatalog configuration, IClock clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public string ConnectorType => "json-file";

    public Task<ConnectorHealth> CheckHealthAsync(
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string path = ResolvePath(source);
            bool exists = File.Exists(path);
            return Task.FromResult(new ConnectorHealth
            {
                Status = exists ? HealthState.Healthy : HealthState.Unhealthy,
                Message = exists ? $"Readable file: {path}" : $"File not found: {path}",
                CheckedAt = _clock.UtcNow,
                Latency = stopwatch.Elapsed
            });
        }
        catch (Exception exception)
        {
            return Task.FromResult(new ConnectorHealth
            {
                Status = HealthState.Unhealthy,
                Message = exception.Message,
                CheckedAt = _clock.UtcNow,
                Latency = stopwatch.Elapsed
            });
        }
    }

    public async Task<ConnectorFetchResult> FetchAsync(
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        string path = ResolvePath(source);
        CuratedItemsDocument document = await AtomicJsonFile.ReadAsync<CuratedItemsDocument>(
            path,
            JsonOptions,
            cancellationToken);
        return new ConnectorFetchResult
        {
            Items = CuratedItemMapper.Map(document)
                .Take(Math.Clamp(source.MaxItemsPerRun ?? 100, 1, 500))
                .ToArray()
        };
    }

    private string ResolvePath(SourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.Endpoint))
        {
            throw new InvalidOperationException("A JSON file endpoint is required.");
        }

        string root = Path.GetFullPath(_configuration.RootPath);
        string candidate = Path.IsPathRooted(source.Endpoint)
            ? Path.GetFullPath(source.Endpoint)
            : Path.GetFullPath(Path.Combine(root, source.Endpoint));
        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidOperationException("JSON source files must be inside the DevSignal root directory.");
        }

        return candidate;
    }
}
