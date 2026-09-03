using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Sources;
using DevSignalStudio.Infrastructure.Common;

namespace DevSignalStudio.Infrastructure.Configuration;

public sealed class JsonConfigurationCatalog : IConfigurationCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Create();
    private static readonly StringComparison FileSystemComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConfigurationCatalog(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ConfigDirectory);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CopySampleIfMissing("sources.sample.json", "sources.json");
            CopySampleIfMissing("ai-providers.sample.json", "ai-providers.json");
            CopySampleIfMissing("profile.sample.json", "profile.json");
            CopySampleIfMissing("curated-items.sample.json", "curated-items.json");

            string[] required =
            {
                "topics.json",
                "content-recipes.json",
                "sources.json",
                "profile.json",
                "ai-providers.json"
            };
            string[] missing = required.Where(file => !File.Exists(PathFor(file))).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidDataException($"Missing configuration files: {string.Join(", ", missing)}.");
            }

            ValidateTopics(await ReadUnlockedAsync<TopicTaxonomyDocument>("topics.json", cancellationToken));
            ValidateRecipes(await ReadUnlockedAsync<ContentRecipesDocument>("content-recipes.json", cancellationToken));
            ValidateSources(await ReadUnlockedAsync<SourcesDocument>("sources.json", cancellationToken));
            ValidateProfile(await ReadUnlockedAsync<ProfileSettingsDocument>("profile.json", cancellationToken));
            ValidateProviders(await ReadUnlockedAsync<AiProvidersDocument>("ai-providers.json", cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<TopicTaxonomyDocument> GetTopicsAsync(CancellationToken cancellationToken) =>
        ReadAsync<TopicTaxonomyDocument>("topics.json", cancellationToken);

    public Task SaveTopicsAsync(TopicTaxonomyDocument topics, CancellationToken cancellationToken)
    {
        ValidateTopics(topics);
        return WriteAsync("topics.json", topics, cancellationToken);
    }

    public Task<ContentRecipesDocument> GetRecipesAsync(CancellationToken cancellationToken) =>
        ReadAsync<ContentRecipesDocument>("content-recipes.json", cancellationToken);

    public Task SaveRecipesAsync(ContentRecipesDocument recipes, CancellationToken cancellationToken)
    {
        ValidateRecipes(recipes);
        return WriteAsync("content-recipes.json", recipes, cancellationToken);
    }

    public Task<ProfileSettingsDocument> GetProfileAsync(CancellationToken cancellationToken) =>
        ReadAsync<ProfileSettingsDocument>("profile.json", cancellationToken);

    public Task SaveProfileAsync(ProfileSettingsDocument profile, CancellationToken cancellationToken)
    {
        ValidateProfile(profile);
        return WriteAsync("profile.json", profile, cancellationToken);
    }

    public Task<AiProvidersDocument> GetAiProvidersAsync(CancellationToken cancellationToken) =>
        ReadAsync<AiProvidersDocument>("ai-providers.json", cancellationToken);

    public Task SaveAiProvidersAsync(AiProvidersDocument providers, CancellationToken cancellationToken)
    {
        ValidateProviders(providers);
        return WriteAsync("ai-providers.json", providers, cancellationToken);
    }

    public Task<SourcesDocument> GetSourcesAsync(CancellationToken cancellationToken) =>
        ReadAsync<SourcesDocument>("sources.json", cancellationToken);

    public Task SaveSourcesAsync(SourcesDocument sources, CancellationToken cancellationToken)
    {
        ValidateSources(sources);
        return WriteAsync("sources.json", sources, cancellationToken);
    }

    public async Task<SourceDefinition?> GetSourceAsync(string id, CancellationToken cancellationToken)
    {
        SourcesDocument document = await GetSourcesAsync(cancellationToken);
        return (document.Sources ?? Array.Empty<SourceDefinition>()).FirstOrDefault(source =>
            source.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private string ConfigDirectory => Path.Combine(RootPath, "config");

    private string PathFor(string file) => Path.Combine(ConfigDirectory, file);

    private async Task<T> ReadAsync<T>(string file, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnlockedAsync<T>(file, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<T> ReadUnlockedAsync<T>(string file, CancellationToken cancellationToken) =>
        AtomicJsonFile.ReadAsync<T>(PathFor(file), JsonOptions, cancellationToken);

    private async Task WriteAsync<T>(string file, T value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await AtomicJsonFile.WriteAsync(PathFor(file), value, JsonOptions, 3, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CopySampleIfMissing(string sample, string target)
    {
        string targetPath = PathFor(target);
        string samplePath = PathFor(sample);
        if (!File.Exists(targetPath) && File.Exists(samplePath))
        {
            File.Copy(samplePath, targetPath);
        }
    }

    private static void ValidateTopics(TopicTaxonomyDocument document)
    {
        IReadOnlyList<TopicPillar> pillars = document.Pillars ?? Array.Empty<TopicPillar>();
        if (document.SchemaVersion < 1 || pillars.Count == 0)
        {
            throw new InvalidDataException("topics.json must contain at least one topic pillar.");
        }
        TopicProfile profile = document.Profile
            ?? throw new InvalidDataException("topics.json requires a profile object.");
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidDataException("topics.json requires profile.id and profile.name.");
        }
        if (profile.DefaultMinimumScore is < 0 or > 100)
        {
            throw new InvalidDataException("topics.json profile.defaultMinimumScore must be between 0 and 100.");
        }
        if (profile.DailyCandidateLimit is < 1 or > 200 ||
            profile.DraftCandidateLimit is < 1 or > 50)
        {
            throw new InvalidDataException("Topic candidate limits are outside the supported range.");
        }

        EnsureUniqueIds(pillars.Select(pillar => pillar.Id), "topic pillar");
        foreach (TopicPillar pillar in pillars)
        {
            IReadOnlyList<TopicKeyword> keywords = pillar.Keywords ?? Array.Empty<TopicKeyword>();
            if (string.IsNullOrWhiteSpace(pillar.Id) || string.IsNullOrWhiteSpace(pillar.Name) || keywords.Count == 0)
            {
                throw new InvalidDataException("Every topic pillar requires an ID, name, and at least one keyword.");
            }
            if (pillar.Priority is < 1 or > 5 || pillar.Weight is < 0 or > 5)
            {
                throw new InvalidDataException($"Topic pillar '{pillar.Id}' has an invalid priority or weight.");
            }
            if (keywords.Any(keyword => string.IsNullOrWhiteSpace(keyword.Term) || keyword.Weight is <= 0 or > 20))
            {
                throw new InvalidDataException($"Topic pillar '{pillar.Id}' has an invalid keyword.");
            }
        }
    }

    private static void ValidateRecipes(ContentRecipesDocument document)
    {
        IReadOnlyList<ContentRecipe> recipes = document.Recipes ?? Array.Empty<ContentRecipe>();
        if (document.SchemaVersion < 1 || recipes.Count == 0)
        {
            throw new InvalidDataException("content-recipes.json must contain at least one recipe.");
        }
        EnsureUniqueIds(recipes.Select(recipe => recipe.Id), "content recipe");
        foreach (ContentRecipe recipe in recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.Id) ||
                string.IsNullOrWhiteSpace(recipe.Name) ||
                string.IsNullOrWhiteSpace(recipe.Channel))
            {
                throw new InvalidDataException("Every content recipe requires an ID, name, and channel.");
            }
            if (recipe.HardMaximumCharacters is <= 0 || recipe.HardMaximumWords is <= 0 ||
                recipe.TargetCharacters is <= 0 || recipe.TargetWords is <= 0)
            {
                throw new InvalidDataException($"Content recipe '{recipe.Id}' contains a non-positive length limit.");
            }
            if (recipe.HashtagRange is not null &&
                (recipe.HashtagRange.Min < 0 ||
                 recipe.HashtagRange.Max < recipe.HashtagRange.Min ||
                 recipe.HashtagRange.Max > 20))
            {
                throw new InvalidDataException($"Content recipe '{recipe.Id}' contains an invalid hashtag range.");
            }
        }
    }

    private static void ValidateSources(SourcesDocument document)
    {
        IReadOnlyList<SourceDefinition> sources = document.Sources ?? Array.Empty<SourceDefinition>();
        if (document.SchemaVersion < 1)
        {
            throw new InvalidDataException("sources.json schemaVersion must be at least 1.");
        }
        EnsureUniqueIds(sources.Select(source => source.Id), "source");
        foreach (SourceDefinition source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) ||
                string.IsNullOrWhiteSpace(source.Name) ||
                string.IsNullOrWhiteSpace(source.ConnectorType))
            {
                throw new InvalidDataException("Every source requires an ID, name, and connectorType.");
            }
            if (source.TrustWeight is < 0 or > 1)
            {
                throw new InvalidDataException($"Source '{source.Id}' trustWeight must be between 0 and 1.");
            }
            if (source.PollMinutes is <= 0 || source.MaxItemsPerRun is <= 0 or > 500)
            {
                throw new InvalidDataException($"Source '{source.Id}' contains an invalid polling or item limit.");
            }
            if (!source.ConnectorType.Equals("manual", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(source.Endpoint))
            {
                throw new InvalidDataException($"Source '{source.Id}' requires an endpoint.");
            }
            if (IsRemoteConnector(source.ConnectorType) && !IsHttpUrl(source.Endpoint))
            {
                throw new InvalidDataException($"Source '{source.Id}' requires an absolute HTTP or HTTPS endpoint.");
            }
        }
    }

    private void ValidateProfile(ProfileSettingsDocument document)
    {
        AuthorProfile profile = document.Profile
            ?? throw new InvalidDataException("profile.json requires a profile object.");
        ScheduleSettings schedule = document.Schedule
            ?? throw new InvalidDataException("profile.json requires a schedule object.");
        StorageSettings storage = document.Storage
            ?? throw new InvalidDataException("profile.json requires a storage object.");
        if (document.SchemaVersion < 1 || string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            throw new InvalidDataException("profile.json requires profile.displayName.");
        }
        if (!TimeOnly.TryParse(schedule.LocalTime, out _))
        {
            throw new InvalidDataException("profile.json schedule.localTime must be a valid local time.");
        }
        if (schedule.MaximumRunsPerDay is < 0 or > 24)
        {
            throw new InvalidDataException("profile.json schedule.maximumRunsPerDay must be between 0 and 24.");
        }
        if (schedule.Enabled && schedule.MaximumRunsPerDay == 0)
        {
            throw new InvalidDataException("An enabled schedule requires maximumRunsPerDay greater than zero.");
        }
        if (!storage.Mode.Equals("JsonSnapshot", StringComparison.OrdinalIgnoreCase) &&
            !storage.Mode.Equals("MemoryOnly", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("profile.json storage.mode must be JsonSnapshot or MemoryOnly.");
        }
        if (storage.BackupCount is < 0 or > 20)
        {
            throw new InvalidDataException("profile.json storage.backupCount must be between 0 and 20.");
        }

        string configuredDirectory = string.IsNullOrWhiteSpace(storage.Directory)
            ? "data"
            : storage.Directory;
        string workspace = Path.IsPathRooted(configuredDirectory)
            ? Path.GetFullPath(configuredDirectory)
            : Path.GetFullPath(Path.Combine(RootPath, configuredDirectory));
        if (!IsInsideRoot(RootPath, workspace))
        {
            throw new InvalidDataException("profile.json storage.directory must remain inside the DevSignal root.");
        }
    }

    private static void ValidateProviders(AiProvidersDocument document)
    {
        IReadOnlyList<AiProviderDefinition> providers = document.Providers ?? Array.Empty<AiProviderDefinition>();
        IReadOnlyList<AiRouteDefinition> routes = document.Routes ?? Array.Empty<AiRouteDefinition>();
        if (document.SchemaVersion < 1 || providers.Count == 0 || routes.Count == 0)
        {
            throw new InvalidDataException("ai-providers.json requires at least one provider and route.");
        }
        EnsureUniqueIds(providers.Select(provider => provider.Id), "AI provider");
        EnsureUniqueIds(routes.Select(route => route.Id), "AI route");

        foreach (AiProviderDefinition provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id) ||
                string.IsNullOrWhiteSpace(provider.Type) ||
                string.IsNullOrWhiteSpace(provider.DisplayName))
            {
                throw new InvalidDataException("Every AI provider requires an ID, type, and displayName.");
            }
            if (provider.Enabled && string.IsNullOrWhiteSpace(provider.Model))
            {
                throw new InvalidDataException($"Enabled AI provider '{provider.Id}' requires a model.");
            }
            if (!string.IsNullOrWhiteSpace(provider.BaseUrl) && !IsHttpUrl(provider.BaseUrl))
            {
                throw new InvalidDataException($"AI provider '{provider.Id}' has an invalid baseUrl.");
            }
            if (!provider.Type.Equals("mock", StringComparison.OrdinalIgnoreCase) &&
                provider.Enabled &&
                string.IsNullOrWhiteSpace(provider.BaseUrl))
            {
                throw new InvalidDataException($"Enabled AI provider '{provider.Id}' requires baseUrl.");
            }
        }

        AiRouteDefinition? defaultRoute = routes.FirstOrDefault(route =>
            route.Id.Equals(document.DefaultRoute, StringComparison.OrdinalIgnoreCase));
        if (defaultRoute is null)
        {
            throw new InvalidDataException($"Default AI route '{document.DefaultRoute}' does not exist.");
        }

        HashSet<string> providerIds = providers
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (AiRouteDefinition route in routes)
        {
            if (string.IsNullOrWhiteSpace(route.Id))
            {
                throw new InvalidDataException("Every AI route requires an ID.");
            }
            IReadOnlyList<string> classify = route.Tasks?.Classify ?? Array.Empty<string>();
            IReadOnlyList<string> draft = route.Tasks?.Draft ?? Array.Empty<string>();
            IReadOnlyList<string> diagram = route.Tasks?.Diagram ?? Array.Empty<string>();
            if (classify.Count == 0 || draft.Count == 0 || diagram.Count == 0)
            {
                throw new InvalidDataException($"AI route '{route.Id}' requires classify, draft, and diagram providers.");
            }

            string[] missing = classify.Concat(draft).Concat(diagram)
                .Where(id => !providerIds.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidDataException(
                    $"AI route '{route.Id}' references unknown providers: {string.Join(", ", missing)}.");
            }
        }
    }

    private static void EnsureUniqueIds(IEnumerable<string> ids, string label)
    {
        string[] duplicates = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidDataException($"Duplicate {label} IDs: {string.Join(", ", duplicates)}.");
        }
    }

    private static bool IsRemoteConnector(string connectorType) =>
        connectorType.Equals("rss", StringComparison.OrdinalIgnoreCase) ||
        connectorType.Equals("stackexchange", StringComparison.OrdinalIgnoreCase) ||
        connectorType.Equals("http-json", StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrWhiteSpace(uri.UserInfo);

    private static bool IsInsideRoot(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedCandidate = Path.GetFullPath(candidate);
        if (string.Equals(normalizedRoot, normalizedCandidate, FileSystemComparison))
        {
            return false;
        }

        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, FileSystemComparison);
    }
}
