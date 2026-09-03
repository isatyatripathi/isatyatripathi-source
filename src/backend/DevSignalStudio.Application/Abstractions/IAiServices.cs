using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Configuration;

namespace DevSignalStudio.Application.Abstractions;

public interface IAiProviderAdapter
{
    string ProviderType { get; }
    Task<AiResponse> GenerateAsync(
        AiProviderDefinition provider,
        AiRequest request,
        CancellationToken cancellationToken);
    Task<AiProviderHealth> CheckHealthAsync(
        AiProviderDefinition provider,
        CancellationToken cancellationToken);
}

public interface IAiRouter
{
    Task<RoutedAiResponse> GenerateAsync(
        string? routeId,
        string task,
        AiRequest request,
        CancellationToken cancellationToken);
    Task<AiProviderHealth> CheckHealthAsync(string providerId, CancellationToken cancellationToken);
}
