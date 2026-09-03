using System.Text.RegularExpressions;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Content;

public sealed partial class SourceService
{
    private readonly IConfigurationCatalog _configuration;
    private readonly IConnectorRegistry _connectors;

    public SourceService(IConfigurationCatalog configuration, IConnectorRegistry connectors)
    {
        _configuration = configuration;
        _connectors = connectors;
    }

    public async Task<IReadOnlyList<SourceDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
        (await _configuration.GetSourcesAsync(cancellationToken)).Sources;

    public async Task<SourceDefinition> GetRequiredAsync(string id, CancellationToken cancellationToken) =>
        await _configuration.GetSourceAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("Source", id);

    public async Task<SourceDefinition> CreateAsync(
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        Validate(source);
        SourcesDocument document = await _configuration.GetSourcesAsync(cancellationToken);
        if (document.Sources.Any(existing => existing.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException("source_exists", $"Source '{source.Id}' already exists.");
        }

        SourcesDocument updated = document with
        {
            Sources = document.Sources.Concat(new[] { source }).OrderBy(item => item.Name).ToArray()
        };
        await _configuration.SaveSourcesAsync(updated, cancellationToken);
        return source;
    }

    public async Task<SourceDefinition> ReplaceAsync(
        string id,
        SourceDefinition source,
        CancellationToken cancellationToken)
    {
        if (!id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("The route ID and source ID must match.");
        }

        Validate(source);
        SourcesDocument document = await _configuration.GetSourcesAsync(cancellationToken);
        if (!document.Sources.Any(existing => existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ResourceNotFoundException("Source", id);
        }

        SourcesDocument updated = document with
        {
            Sources = document.Sources
                .Select(existing => existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ? source : existing)
                .ToArray()
        };
        await _configuration.SaveSourcesAsync(updated, cancellationToken);
        return source;
    }

    public async Task<SourceDefinition> SetEnabledAsync(
        string id,
        bool enabled,
        CancellationToken cancellationToken)
    {
        SourceDefinition current = await GetRequiredAsync(id, cancellationToken);
        return await ReplaceAsync(id, current with { Enabled = enabled }, cancellationToken);
    }

    public async Task<SourceTestResult> TestAsync(string id, CancellationToken cancellationToken)
    {
        SourceDefinition source = await GetRequiredAsync(id, cancellationToken);
        IContentConnector connector = _connectors.GetRequired(source.ConnectorType);
        ConnectorHealth health = await connector.CheckHealthAsync(source, cancellationToken);
        if (health.Status == HealthState.Unhealthy || health.Status == HealthState.Disabled)
        {
            return new SourceTestResult { Health = health };
        }

        ConnectorFetchResult result = await connector.FetchAsync(
            source with { MaxItemsPerRun = Math.Min(source.MaxItemsPerRun ?? 3, 3) },
            cancellationToken);

        return new SourceTestResult
        {
            Health = health,
            Preview = result.Items.Take(3).ToArray(),
            Warnings = result.Warnings
        };
    }

    private void Validate(SourceDefinition source)
    {
        Dictionary<string, string[]> errors = new();
        if (string.IsNullOrWhiteSpace(source.Id) || !SourceIdRegex().IsMatch(source.Id))
        {
            errors["id"] = new[] { "Use lowercase letters, numbers, and hyphens." };
        }
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            errors["name"] = new[] { "Name is required." };
        }
        if (!_connectors.ConnectorTypes.Contains(source.ConnectorType, StringComparer.OrdinalIgnoreCase))
        {
            errors["connectorType"] = new[] { $"Unsupported connector type '{source.ConnectorType}'." };
        }
        if (source.TrustWeight is < 0 or > 1)
        {
            errors["trustWeight"] = new[] { "Trust weight must be between 0 and 1." };
        }
        if (!source.ConnectorType.Equals("manual", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(source.Endpoint))
        {
            errors["endpoint"] = new[] { "Endpoint is required for this connector." };
        }
        if (source.PollMinutes is <= 0)
        {
            errors["pollMinutes"] = new[] { "Poll interval must be greater than zero when supplied." };
        }
        if (source.MaxItemsPerRun is <= 0 or > 500)
        {
            errors["maxItemsPerRun"] = new[] { "Item limit must be between 1 and 500 when supplied." };
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException("The source definition is invalid.", errors);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.Compiled)]
    private static partial Regex SourceIdRegex();
}
