using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Abstractions;

public interface IConfigurationCatalog
{
    string RootPath { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<TopicTaxonomyDocument> GetTopicsAsync(CancellationToken cancellationToken);
    Task SaveTopicsAsync(TopicTaxonomyDocument topics, CancellationToken cancellationToken);
    Task<ContentRecipesDocument> GetRecipesAsync(CancellationToken cancellationToken);
    Task SaveRecipesAsync(ContentRecipesDocument recipes, CancellationToken cancellationToken);
    Task<ProfileSettingsDocument> GetProfileAsync(CancellationToken cancellationToken);
    Task SaveProfileAsync(ProfileSettingsDocument profile, CancellationToken cancellationToken);
    Task<AiProvidersDocument> GetAiProvidersAsync(CancellationToken cancellationToken);
    Task SaveAiProvidersAsync(AiProvidersDocument providers, CancellationToken cancellationToken);
    Task<SourcesDocument> GetSourcesAsync(CancellationToken cancellationToken);
    Task SaveSourcesAsync(SourcesDocument sources, CancellationToken cancellationToken);
    Task<SourceDefinition?> GetSourceAsync(string id, CancellationToken cancellationToken);
}
