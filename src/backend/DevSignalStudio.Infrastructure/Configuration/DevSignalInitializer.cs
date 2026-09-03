using DevSignalStudio.Application.Abstractions;

namespace DevSignalStudio.Infrastructure.Configuration;

public sealed class DevSignalInitializer
{
    private readonly IConfigurationCatalog _configuration;
    private readonly IContentWorkspace _workspace;

    public DevSignalInitializer(
        IConfigurationCatalog configuration,
        IContentWorkspace workspace)
    {
        _configuration = configuration;
        _workspace = workspace;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _configuration.InitializeAsync(cancellationToken);
        await _workspace.InitializeAsync(cancellationToken);
    }
}
