using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Content;
using DevSignalStudio.Application.Dashboard;
using DevSignalStudio.Application.Drafting;
using DevSignalStudio.Application.Ingestion;
using DevSignalStudio.Infrastructure.Ai;
using DevSignalStudio.Infrastructure.Common;
using DevSignalStudio.Infrastructure.Configuration;
using DevSignalStudio.Infrastructure.Persistence;
using DevSignalStudio.Infrastructure.Security;
using DevSignalStudio.Infrastructure.Sources;
using DevSignalStudio.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace DevSignalStudio.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDevSignalStudioBackend(
        this IServiceCollection services,
        string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();

        services.AddSingleton<IConfigurationCatalog>(_ => new JsonConfigurationCatalog(rootPath));
        services.AddSingleton<IContentWorkspace, LocalContentWorkspace>();
        services.AddSingleton<DevSignalInitializer>();

        services.AddSingleton<UrlSafetyValidator>();
        services.AddSingleton<SafeHttpFetcher>();
        services.AddSingleton<AiHttpClient>();
        services.AddSingleton<IMermaidSanitizer, MermaidSanitizer>();

        services.AddSingleton<IContentConnector, RssConnector>();
        services.AddSingleton<IContentConnector, StackExchangeConnector>();
        services.AddSingleton<IContentConnector, JsonFileConnector>();
        services.AddSingleton<IContentConnector, HttpJsonConnector>();
        services.AddSingleton<IContentConnector, ManualConnector>();
        services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();

        services.AddSingleton<MockAiProviderAdapter>();
        services.AddSingleton<OllamaAiProviderAdapter>();
        services.AddSingleton<AnthropicAiProviderAdapter>();
        services.AddSingleton<IAiProviderAdapter>(provider =>
            provider.GetRequiredService<MockAiProviderAdapter>());
        services.AddSingleton<IAiProviderAdapter>(provider =>
            provider.GetRequiredService<OllamaAiProviderAdapter>());
        services.AddSingleton<IAiProviderAdapter>(provider =>
            provider.GetRequiredService<AnthropicAiProviderAdapter>());
        services.AddSingleton<IAiProviderAdapter>(provider => new OpenAiChatProviderAdapter(
            provider.GetRequiredService<AiHttpClient>(),
            provider.GetRequiredService<IClock>(),
            "openai",
            forceJsonMode: true));
        services.AddSingleton<IAiProviderAdapter>(provider => new OpenAiChatProviderAdapter(
            provider.GetRequiredService<AiHttpClient>(),
            provider.GetRequiredService<IClock>(),
            "openai-compatible",
            forceJsonMode: false));
        services.AddSingleton<IAiRouter, AiRouter>();

        services.AddSingleton<IIngestionRunQueue, IngestionRunQueue>();
        services.AddSingleton<IDraftGenerationQueue, DraftGenerationQueue>();
        services.AddSingleton<IRunCancellationRegistry, RunCancellationRegistry>();

        services.AddSingleton<ContentNormalizer>();
        services.AddSingleton<IRelevanceScorer, DeterministicRelevanceScorer>();
        services.AddSingleton<ContentService>();
        services.AddSingleton<SourceService>();
        services.AddSingleton<PromptComposer>();
        services.AddSingleton<DraftOutputParser>();
        services.AddSingleton<DraftValidator>();
        services.AddSingleton<DraftService>();
        services.AddSingleton<IngestionRunService>();
        services.AddSingleton<DraftGenerationRunService>();
        services.AddSingleton<IngestionOrchestrator>();
        services.AddSingleton<DraftGenerationOrchestrator>();
        services.AddSingleton<DashboardService>();

        services.AddHostedService<IngestionWorker>();
        services.AddHostedService<DraftGenerationWorker>();
        services.AddHostedService<DailyIngestionScheduler>();

        return services;
    }
}
