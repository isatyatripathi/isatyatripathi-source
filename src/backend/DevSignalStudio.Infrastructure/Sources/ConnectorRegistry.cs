using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Common;

namespace DevSignalStudio.Infrastructure.Sources;

public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly IReadOnlyDictionary<string, IContentConnector> _connectors;

    public ConnectorRegistry(IEnumerable<IContentConnector> connectors)
    {
        _connectors = connectors.ToDictionary(
            connector => connector.ConnectorType,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> ConnectorTypes => _connectors.Keys.OrderBy(key => key).ToArray();

    public IContentConnector GetRequired(string connectorType) =>
        _connectors.TryGetValue(connectorType, out IContentConnector? connector)
            ? connector
            : throw new DomainRuleException(
                "connector_not_registered",
                $"No connector is registered for type '{connectorType}'.");
}
