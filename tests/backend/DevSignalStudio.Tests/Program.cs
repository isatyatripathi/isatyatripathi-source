using System.Text.Json;
using System.Text.Json.Serialization;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Content;
using DevSignalStudio.Application.Drafting;
using DevSignalStudio.Application.Ingestion;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Drafting;
using DevSignalStudio.Domain.Runs;
using DevSignalStudio.Domain.Sources;
using DevSignalStudio.Infrastructure.Ai;
using DevSignalStudio.Infrastructure.Configuration;
using DevSignalStudio.Infrastructure.Persistence;
using DevSignalStudio.Infrastructure.Security;
using DevSignalStudio.Infrastructure.Sources;
using DevSignalStudio.Infrastructure.Workers;

namespace DevSignalStudio.Tests;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        List<(string Name, Func<Task> Test)> tests = new()
        {
            ("Canonical URL removes tracking parameters", TestCanonicalUrlAsync),
            ("Mermaid sanitizer blocks unsafe directives", TestMermaidSanitizerAsync),
            ("URL safety permits loopback only when requested", TestUrlSafetyAsync),
            ("JSON connector reads curated items", TestJsonConnectorAsync),
            ("Workspace persists and reloads content", TestWorkspacePersistenceAsync),
            ("Relevance scorer recognizes .NET and AI", TestRelevanceScorerAsync),
            ("Configuration rejects duplicate IDs", TestDuplicateConfigurationIdsAsync),
            ("Configuration prevents storage path escape", TestStoragePathEscapeAsync),
            ("Manual connector type validation is case-insensitive", TestManualSourceValidationAsync),
            ("Manual capture rejects unknown source IDs", TestUnknownManualSourceAsync),
            ("Publication lifecycle validates the published URL", TestPublicationLifecycleAsync),
            ("Local ingestion and mock drafting work end-to-end", TestEndToEndPipelineAsync)
        };

        int failed = 0;
        foreach ((string name, Func<Task> test) in tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {name}");
                Console.Error.WriteLine(exception);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} smoke checks passed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task TestCanonicalUrlAsync()
    {
        string? result = ContentIdentity.CanonicalizeUrl(
            "HTTPS://Example.com:443/path/?utm_source=feed&b=2&a=1#section");
        AssertEqual("https://example.com/path?a=1&b=2", result);
        return Task.CompletedTask;
    }

    private static Task TestMermaidSanitizerAsync()
    {
        MermaidSanitizer sanitizer = new();
        MermaidSanitizationResult safe = sanitizer.Sanitize("flowchart LR\n A[Source] --> B[Review]");
        Assert(safe.IsValid, "Expected a normal flowchart to be accepted.");

        MermaidSanitizationResult unsafeDiagram = sanitizer.Sanitize(
            "%%{init: {\"securityLevel\": \"loose\"}}%%\nflowchart LR\nclick A https://evil.test");
        Assert(!unsafeDiagram.IsValid, "Expected unsafe Mermaid directives to be rejected.");
        return Task.CompletedTask;
    }

    private static async Task TestUrlSafetyAsync()
    {
        UrlSafetyValidator validator = new();
        Uri loopback = await validator.ValidateAsync(
            "http://127.0.0.1:11434",
            allowLoopback: true,
            CancellationToken.None);
        AssertEqual("127.0.0.1", loopback.Host);

        await AssertThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            "http://127.0.0.1:11434",
            allowLoopback: false,
            CancellationToken.None));
        await AssertThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            "http://10.10.0.8/service",
            allowLoopback: true,
            CancellationToken.None));
    }

    private static async Task TestJsonConnectorAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("MemoryOnly");
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);
        JsonFileConnector connector = new(configuration, new FixedClock());
        SourceDefinition source = (await configuration.GetSourcesAsync(CancellationToken.None)).Sources.Single();
        ConnectorFetchResult result = await connector.FetchAsync(source, CancellationToken.None);
        AssertEqual(1, result.Items.Count);
        AssertEqual("A local .NET and AI note", result.Items[0].Title);
    }

    private static async Task TestWorkspacePersistenceAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("JsonSnapshot");
        FixedClock clock = new();
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);

        LocalContentWorkspace first = new(configuration, clock);
        await first.InitializeAsync(CancellationToken.None);
        ContentItem item = new()
        {
            Id = "item_test",
            SourceId = "curated-local",
            SourceName = "Curated local JSON",
            ExternalId = "one",
            Title = "Persist me",
            CollectedAt = clock.UtcNow,
            ContentFingerprint = "fingerprint"
        };
        await first.SaveItemAsync(item, CancellationToken.None);

        LocalContentWorkspace second = new(configuration, clock);
        await second.InitializeAsync(CancellationToken.None);
        ContentItem? restored = await second.GetItemAsync(item.Id, CancellationToken.None);
        Assert(restored is not null, "Expected the saved content item to be restored.");
        AssertEqual(item.Title, restored!.Title);
    }

    private static async Task TestRelevanceScorerAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("MemoryOnly");
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);
        TopicTaxonomyDocument taxonomy = await configuration.GetTopicsAsync(CancellationToken.None);
        SourceDefinition source = (await configuration.GetSourcesAsync(CancellationToken.None)).Sources.Single();
        ContentItem item = new()
        {
            Id = "item_score",
            SourceId = source.Id,
            SourceName = source.Name,
            ExternalId = "score",
            Title = "Build an AI-powered ASP.NET Core API with C# and MCP",
            Summary = "A practical architecture guide with security and performance trade-offs.",
            Tags = new[] { "dotnet", "ai", "mcp" },
            CollectedAt = new FixedClock().UtcNow,
            ContentFingerprint = "score-fingerprint"
        };

        DeterministicRelevanceScorer scorer = new(new FixedClock());
        (ContentScore score, IReadOnlyList<TopicMatch> matches) =
            scorer.Score(item, source, taxonomy, Array.Empty<ContentItem>());
        Assert(score.FinalScore >= 50, $"Expected a strong score, got {score.FinalScore}.");
        Assert(matches.Any(match => match.PillarId == "dotnet-csharp"), "Expected a .NET match.");
        Assert(matches.Any(match => match.PillarId == "ai-engineering"), "Expected an AI match.");
    }

    private static async Task TestDuplicateConfigurationIdsAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("MemoryOnly");
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);
        TopicTaxonomyDocument topics = await configuration.GetTopicsAsync(CancellationToken.None);
        TopicTaxonomyDocument invalid = topics with
        {
            Pillars = topics.Pillars.Concat(new[] { topics.Pillars[0] }).ToArray()
        };

        await AssertThrowsAsync<InvalidDataException>(() =>
            configuration.SaveTopicsAsync(invalid, CancellationToken.None));
    }

    private static async Task TestStoragePathEscapeAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("MemoryOnly");
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);
        ProfileSettingsDocument profile = await configuration.GetProfileAsync(CancellationToken.None);
        ProfileSettingsDocument invalid = profile with
        {
            Storage = profile.Storage with { Directory = "../outside-devsignal" }
        };

        await AssertThrowsAsync<InvalidDataException>(() =>
            configuration.SaveProfileAsync(invalid, CancellationToken.None));
    }

    private static async Task TestManualSourceValidationAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("MemoryOnly");
        FixedClock clock = new();
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);
        ConnectorRegistry registry = new(new IContentConnector[] { new ManualConnector(clock) });
        SourceService service = new(configuration, registry);

        SourceDefinition created = await service.CreateAsync(
            new SourceDefinition
            {
                Id = "manual-check",
                Name = "Manual check",
                ConnectorType = "Manual",
                Enabled = true,
                TrustWeight = 0.8
            },
            CancellationToken.None);

        AssertEqual("manual-check", created.Id);
        Assert(created.Endpoint is null, "A manual source should not require an endpoint.");
    }

    private static async Task TestUnknownManualSourceAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("MemoryOnly");
        FixedClock clock = new();
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);
        LocalContentWorkspace workspace = new(configuration, clock);
        await workspace.InitializeAsync(CancellationToken.None);
        ContentService service = new(
            configuration,
            workspace,
            new ContentNormalizer(clock),
            new DeterministicRelevanceScorer(clock),
            clock);

        await AssertThrowsAsync<ResourceNotFoundException>(() => service.AddManualAsync(
            new ManualContentRequest
            {
                SourceId = "typo-source",
                Title = "This source ID should be rejected"
            },
            CancellationToken.None));
    }

    private static Task TestPublicationLifecycleAsync()
    {
        FixedClock clock = new();
        Draft draft = new()
        {
            Id = "draft_lifecycle",
            Status = DraftStatus.InReview,
            Revision = 1,
            Revisions = new[]
            {
                new DraftRevision
                {
                    Revision = 1,
                    Content = new DraftContent { Hook = "Hook", Body = "Body" },
                    SavedAt = clock.UtcNow
                }
            },
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };

        Draft approved = draft.Approve(1, ValidationReport.Success(clock.UtcNow), clock.UtcNow);
        AssertThrows<DomainRuleException>(() => approved.MarkPublished(
            1,
            "linkedin",
            "javascript:alert(1)",
            clock.UtcNow,
            clock.UtcNow));
        Draft published = approved.MarkPublished(
            1,
            "linkedin",
            "https://www.linkedin.com/posts/example",
            clock.UtcNow,
            clock.UtcNow);
        AssertEqual(DraftStatus.Published, published.Status);
        return Task.CompletedTask;
    }

    private static async Task TestEndToEndPipelineAsync()
    {
        await using TestRoot root = await TestRoot.CreateAsync("MemoryOnly");
        FixedClock clock = new();
        FixedIdGenerator ids = new();
        JsonConfigurationCatalog configuration = new(root.Path);
        await configuration.InitializeAsync(CancellationToken.None);
        LocalContentWorkspace workspace = new(configuration, clock);
        await workspace.InitializeAsync(CancellationToken.None);

        JsonFileConnector jsonConnector = new(configuration, clock);
        ManualConnector manualConnector = new(clock);
        ConnectorRegistry connectors = new(new IContentConnector[] { jsonConnector, manualConnector });
        using RunCancellationRegistry cancellations = new();
        DraftGenerationRunService draftRuns = new(
            workspace,
            new DraftGenerationQueue(),
            cancellations,
            clock,
            ids);
        IngestionOrchestrator ingestion = new(
            configuration,
            workspace,
            connectors,
            new ContentNormalizer(clock),
            new DeterministicRelevanceScorer(clock),
            draftRuns,
            clock);

        IngestionRun ingestionRun = new()
        {
            Id = "ing_e2e",
            Status = RunStatus.Queued,
            Trigger = "test",
            Request = new IngestionRunRequest
            {
                SourceIds = new[] { "curated-local" },
                Force = true,
                GenerateDrafts = false,
                MaxCandidates = 10
            },
            CreatedAt = clock.UtcNow
        };
        await workspace.SaveIngestionRunAsync(ingestionRun, CancellationToken.None);
        await ingestion.ExecuteAsync(ingestionRun.Id, CancellationToken.None);

        IngestionRun completedIngestion = await workspace.GetIngestionRunAsync(
            ingestionRun.Id,
            CancellationToken.None) ?? throw new InvalidOperationException("Ingestion run was not saved.");
        Assert(
            completedIngestion.Status is RunStatus.Completed or RunStatus.CompletedWithWarnings,
            $"Unexpected ingestion status: {completedIngestion.Status}.");
        IReadOnlyList<ContentItem> items = await workspace.GetAllItemsAsync(CancellationToken.None);
        AssertEqual(1, items.Count);

        DraftGenerationRun generationRun = new()
        {
            Id = "gen_e2e",
            Status = RunStatus.Queued,
            Request = new DraftGenerationRequest
            {
                ContentItemIds = new[] { items[0].Id },
                RecipeId = "linkedin-explainer",
                ProviderRoute = "offline"
            },
            CreatedAt = clock.UtcNow
        };
        await workspace.SaveDraftGenerationRunAsync(generationRun, CancellationToken.None);

        MermaidSanitizer mermaid = new();
        DraftValidator draftValidator = new(mermaid, clock);
        MockAiProviderAdapter mock = new(clock);
        AiRouter router = new(configuration, clock, new IAiProviderAdapter[] { mock });
        DraftGenerationOrchestrator drafting = new(
            workspace,
            configuration,
            router,
            mermaid,
            new PromptComposer(),
            new DraftOutputParser(),
            draftValidator,
            clock,
            ids);
        await drafting.ExecuteAsync(generationRun.Id, CancellationToken.None);

        DraftGenerationRun completedGeneration = await workspace.GetDraftGenerationRunAsync(
            generationRun.Id,
            CancellationToken.None) ?? throw new InvalidOperationException("Draft generation run was not saved.");
        Assert(
            completedGeneration.Status is RunStatus.Completed or RunStatus.CompletedWithWarnings,
            $"Unexpected generation status: {completedGeneration.Status}.");
        Assert(!string.IsNullOrWhiteSpace(completedGeneration.DraftId), "Expected a generated draft ID.");

        Draft draft = await workspace.GetDraftAsync(
            completedGeneration.DraftId!,
            CancellationToken.None) ?? throw new InvalidOperationException("Generated draft was not saved.");
        AssertEqual(DraftStatus.InReview, draft.Status);
        Assert(draft.Validation.IsValid, "The deterministic mock draft should pass blocking validation.");
        Assert(!string.IsNullOrWhiteSpace(draft.LatestRevision?.Content.Mermaid), "Expected a Mermaid diagram.");
        AssertEqual("mock", draft.Generation?.ProviderType);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedIdGenerator : IIdGenerator
    {
        private int _value;

        public string NewId(string prefix) => $"{prefix}_{Interlocked.Increment(ref _value):D4}";
    }

    private sealed class TestRoot : IAsyncDisposable
    {
        private TestRoot(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static async Task<TestRoot> CreateAsync(string storageMode)
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "devsignal-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "config"));

            TopicTaxonomyDocument topics = new()
            {
                Profile = new TopicProfile
                {
                    Id = "test",
                    Name = "Test profile",
                    DefaultMinimumScore = 50,
                    DailyCandidateLimit = 10,
                    DraftCandidateLimit = 3
                },
                Pillars = new[]
                {
                    new TopicPillar
                    {
                        Id = "dotnet-csharp",
                        Name = ".NET and C#",
                        Priority = 1,
                        Weight = 1,
                        Keywords = new[]
                        {
                            new TopicKeyword { Term = ".NET", Weight = 5 },
                            new TopicKeyword { Term = "ASP.NET Core", Weight = 5 },
                            new TopicKeyword { Term = "C#", Weight = 4 }
                        }
                    },
                    new TopicPillar
                    {
                        Id = "ai-engineering",
                        Name = "AI Engineering",
                        Priority = 1,
                        Weight = 1,
                        Keywords = new[]
                        {
                            new TopicKeyword { Term = "AI", Weight = 5 },
                            new TopicKeyword { Term = "MCP", Weight = 5 }
                        }
                    }
                }
            };
            ContentRecipesDocument recipes = new()
            {
                Recipes = new[]
                {
                    new ContentRecipe
                    {
                        Id = "linkedin-explainer",
                        Name = "LinkedIn explainer",
                        Channel = "linkedin",
                        Enabled = true
                    }
                }
            };
            ProfileSettingsDocument profile = new()
            {
                Profile = new AuthorProfile { DisplayName = "Test user" },
                Schedule = new ScheduleSettings { Enabled = false, LocalTime = "07:00" },
                Storage = new StorageSettings
                {
                    Mode = storageMode,
                    Directory = "data",
                    BackupCount = 1
                }
            };
            SourcesDocument sources = new()
            {
                Sources = new[]
                {
                    new SourceDefinition
                    {
                        Id = "curated-local",
                        Name = "Curated local JSON",
                        ConnectorType = "json-file",
                        Endpoint = "config/curated-items.json",
                        Enabled = true,
                        TrustWeight = 0.9
                    }
                }
            };
            AiProvidersDocument providers = new()
            {
                DefaultRoute = "offline",
                Providers = new[]
                {
                    new AiProviderDefinition
                    {
                        Id = "mock",
                        Type = "mock",
                        DisplayName = "Mock",
                        Enabled = true,
                        Model = "template-v1"
                    }
                },
                Routes = new[]
                {
                    new AiRouteDefinition
                    {
                        Id = "offline",
                        Tasks = new AiTaskRoutes
                        {
                            Classify = new[] { "mock" },
                            Draft = new[] { "mock" },
                            Diagram = new[] { "mock" }
                        }
                    }
                }
            };
            CuratedItemsDocument curated = new()
            {
                Items = new[]
                {
                    new CuratedItem
                    {
                        Id = "note-1",
                        Title = "A local .NET and AI note",
                        Summary = "Testing the configurable JSON source.",
                        Content = "Build an ASP.NET Core AI workflow and explain the trade-offs.",
                        Tags = new[] { "dotnet", "ai" }
                    }
                }
            };

            await WriteAsync(root, "topics.json", topics);
            await WriteAsync(root, "content-recipes.json", recipes);
            await WriteAsync(root, "profile.json", profile);
            await WriteAsync(root, "sources.json", sources);
            await WriteAsync(root, "ai-providers.json", providers);
            await WriteAsync(root, "curated-items.json", curated);
            return new TestRoot(root);
        }

        private static Task WriteAsync<T>(string root, string name, T value) =>
            File.WriteAllTextAsync(
                System.IO.Path.Combine(root, "config", name),
                JsonSerializer.Serialize(value, JsonOptions));

        public ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Cleanup should not hide a useful smoke-test failure.
            }
            return ValueTask.CompletedTask;
        }
    }
}
