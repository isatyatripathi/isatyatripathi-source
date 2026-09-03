using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Abstractions;

public interface IContentConnector
{
    string ConnectorType { get; }
    Task<ConnectorHealth> CheckHealthAsync(SourceDefinition source, CancellationToken cancellationToken);
    Task<ConnectorFetchResult> FetchAsync(SourceDefinition source, CancellationToken cancellationToken);
}

public interface IConnectorRegistry
{
    IContentConnector GetRequired(string connectorType);
    IReadOnlyList<string> ConnectorTypes { get; }
}
