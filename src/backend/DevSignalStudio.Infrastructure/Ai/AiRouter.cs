using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;

namespace DevSignalStudio.Infrastructure.Ai;

public sealed class AiRouter : IAiRouter
{
    private readonly IConfigurationCatalog _configuration;
    private readonly IClock _clock;
    private readonly IReadOnlyDictionary<string, IAiProviderAdapter> _adapters;

    public AiRouter(
        IConfigurationCatalog configuration,
        IClock clock,
        IEnumerable<IAiProviderAdapter> adapters)
    {
        _configuration = configuration;
        _clock = clock;
        _adapters = adapters.ToDictionary(
            adapter => adapter.ProviderType,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RoutedAiResponse> GenerateAsync(
        string? routeId,
        string task,
        AiRequest request,
        CancellationToken cancellationToken)
    {
        AiProvidersDocument document = await _configuration.GetAiProvidersAsync(cancellationToken);
        string selectedRouteId = string.IsNullOrWhiteSpace(routeId)
            ? document.DefaultRoute
            : routeId.Trim();
        AiRouteDefinition? route = (document.Routes ?? Array.Empty<AiRouteDefinition>()).FirstOrDefault(candidate =>
            candidate.Id.Equals(selectedRouteId, StringComparison.OrdinalIgnoreCase));
        if (route is null)
        {
            AiProviderDefinition? selectedProvider = (document.Providers ?? Array.Empty<AiProviderDefinition>())
                .FirstOrDefault(provider => provider.Id.Equals(selectedRouteId, StringComparison.OrdinalIgnoreCase));
            route = selectedProvider is null
                ? throw new ResourceNotFoundException("AI route or provider", selectedRouteId)
                : new AiRouteDefinition
                {
                    Id = selectedProvider.Id,
                    Tasks = new AiTaskRoutes
                    {
                        Classify = new[] { selectedProvider.Id },
                        Draft = new[] { selectedProvider.Id },
                        Diagram = new[] { selectedProvider.Id }
                    }
                };
        }

        IReadOnlyList<string> providerIds = (route.Tasks ?? new AiTaskRoutes()).ForTask(task);
        if (providerIds.Count == 0)
        {
            throw new DomainRuleException(
                "ai_route_empty",
                $"Route '{route.Id}' has no providers configured for task '{task}'.");
        }

        IReadOnlyList<AiProviderDefinition> providers = document.Providers ?? Array.Empty<AiProviderDefinition>();
        List<string> fallbackErrors = new();
        foreach (string providerId in providerIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AiProviderDefinition? provider = providers.FirstOrDefault(candidate =>
                candidate.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                fallbackErrors.Add($"Provider '{providerId}' does not exist.");
                continue;
            }
            if (!provider.Enabled)
            {
                fallbackErrors.Add($"Provider '{provider.Id}' is disabled.");
                continue;
            }
            if (!_adapters.TryGetValue(provider.Type, out IAiProviderAdapter? adapter))
            {
                fallbackErrors.Add(
                    $"No adapter is registered for provider type '{provider.Type}' ({provider.Id}).");
                continue;
            }

            try
            {
                AiResponse response = await adapter.GenerateAsync(provider, request, cancellationToken);
                return new RoutedAiResponse
                {
                    Response = response,
                    RouteId = route.Id,
                    FallbackErrors = fallbackErrors.ToArray()
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                fallbackErrors.Add($"{provider.DisplayName} ({provider.Id}): {exception.Message}");
            }
        }

        throw new DomainRuleException(
            "ai_route_exhausted",
            $"All providers in route '{route.Id}' failed for task '{task}'. " +
            string.Join(" | ", fallbackErrors));
    }

    public async Task<AiProviderHealth> CheckHealthAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        AiProvidersDocument document = await _configuration.GetAiProvidersAsync(cancellationToken);
        AiProviderDefinition provider = (document.Providers ?? Array.Empty<AiProviderDefinition>()).FirstOrDefault(candidate =>
                candidate.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ResourceNotFoundException("AI provider", providerId);

        if (!_adapters.TryGetValue(provider.Type, out IAiProviderAdapter? adapter))
        {
            return new AiProviderHealth
            {
                ProviderId = provider.Id,
                Status = HealthState.Unhealthy,
                Message = $"No adapter is registered for provider type '{provider.Type}'.",
                CheckedAt = _clock.UtcNow
            };
        }

        return await adapter.CheckHealthAsync(provider, cancellationToken);
    }
}
