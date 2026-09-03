using System.Text;
using DevSignalStudio.Api.Models;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Content;
using DevSignalStudio.Application.Dashboard;
using DevSignalStudio.Application.Drafting;
using DevSignalStudio.Application.Ingestion;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Runs;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Api.Endpoints;

public static class ApiEndpoints
{
    public static WebApplication MapDevSignalEndpoints(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api/v1");
        api.MapGet("/", () => Results.Ok(new
        {
            name = "DevSignal Studio API",
            version = "0.1.0",
            resources = new
            {
                dashboard = "/api/v1/dashboard",
                sources = "/api/v1/sources",
                ingestionRuns = "/api/v1/ingestion/runs",
                items = "/api/v1/items",
                drafts = "/api/v1/drafts",
                providers = "/api/v1/providers",
                settings = "/api/v1/settings",
                health = "/api/v1/health/ready"
            }
        }));

        MapDashboard(api);
        MapSources(api);
        MapIngestion(api);
        MapContentItems(api);
        MapDrafts(api);
        MapConfiguration(api);
        MapRuns(api);
        MapHealth(app, api);

        return app;
    }

    private static void MapDashboard(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (
            DashboardService dashboard,
            CancellationToken cancellationToken) =>
            Results.Ok(await dashboard.GetAsync(cancellationToken)));
    }

    private static void MapSources(RouteGroupBuilder api)
    {
        RouteGroupBuilder sources = api.MapGroup("/sources");

        sources.MapGet("/", async (
            HttpRequest request,
            SourceService service,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<SourceDefinition> values = await service.GetAllAsync(cancellationToken);
            string? kind = request.Query["kind"].FirstOrDefault();
            string? enabledText = request.Query["enabled"].FirstOrDefault();
            IEnumerable<SourceDefinition> filtered = values;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                filtered = filtered.Where(source =>
                    source.ConnectorType.Equals(kind, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(enabledText))
            {
                if (!bool.TryParse(enabledText, out bool enabled))
                {
                    throw new RequestValidationException(
                        "enabled must be true or false.",
                        new Dictionary<string, string[]> { ["enabled"] = new[] { "Use true or false." } });
                }
                filtered = filtered.Where(source => source.Enabled == enabled);
            }
            return Results.Ok(filtered.OrderBy(source => source.Name).ToArray());
        });

        sources.MapGet("/{id}", async (
            string id,
            SourceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetRequiredAsync(id, cancellationToken)));

        sources.MapPost("/", async (
            SourceDefinition source,
            SourceService service,
            CancellationToken cancellationToken) =>
        {
            SourceDefinition created = await service.CreateAsync(source, cancellationToken);
            return Results.Created($"/api/v1/sources/{created.Id}", created);
        });

        sources.MapPut("/{id}", async (
            string id,
            SourceDefinition source,
            SourceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ReplaceAsync(id, source, cancellationToken)));

        sources.MapPost("/{id}/enable", async (
            string id,
            SourceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.SetEnabledAsync(id, true, cancellationToken)));

        sources.MapPost("/{id}/disable", async (
            string id,
            SourceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.SetEnabledAsync(id, false, cancellationToken)));

        sources.MapPost("/{id}/test", async (
            string id,
            SourceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.TestAsync(id, cancellationToken)));
    }

    private static void MapIngestion(RouteGroupBuilder api)
    {
        RouteGroupBuilder runs = api.MapGroup("/ingestion/runs");

        runs.MapGet("/", async (
            HttpRequest request,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
            Results.Ok(await workspace.QueryIngestionRunsAsync(
                ApiQueryParser.ParseRun(request),
                cancellationToken)));

        runs.MapPost("/", async (
            IngestionRunRequest request,
            IngestionRunService service,
            CancellationToken cancellationToken) =>
        {
            IngestionRun run = await service.StartAsync(request, "manual", cancellationToken);
            return Results.Accepted($"/api/v1/ingestion/runs/{run.Id}", run);
        });

        runs.MapGet("/{id}", async (
            string id,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
        {
            IngestionRun run = await workspace.GetIngestionRunAsync(id, cancellationToken)
                ?? throw new ResourceNotFoundException("Ingestion run", id);
            return Results.Ok(run);
        });

        runs.MapPost("/{id}/cancel", async (
            string id,
            IngestionRunService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CancelAsync(id, cancellationToken)));
    }

    private static void MapContentItems(RouteGroupBuilder api)
    {
        RouteGroupBuilder items = api.MapGroup("/items");

        items.MapGet("/", async (
            HttpRequest request,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
            Results.Ok(await workspace.QueryItemsAsync(
                ApiQueryParser.ParseContent(request),
                cancellationToken)));

        items.MapGet("/{id}", async (
            string id,
            ContentService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetRequiredAsync(id, cancellationToken)));

        items.MapPost("/manual", async (
            ManualContentRequest request,
            ContentService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.AddManualAsync(request, cancellationToken);
            return Results.Created($"/api/v1/items/{created.Id}", created);
        });

        items.MapPost("/{id}/promote", async (
            string id,
            ContentService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.PromoteAsync(id, cancellationToken)));

        items.MapPost("/{id}/archive", async (
            string id,
            ContentService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ArchiveAsync(id, cancellationToken)));
    }

    private static void MapDrafts(RouteGroupBuilder api)
    {
        RouteGroupBuilder drafts = api.MapGroup("/drafts");

        drafts.MapGet("/", async (
            HttpRequest request,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
            Results.Ok(await workspace.QueryDraftsAsync(
                ApiQueryParser.ParseDraft(request),
                cancellationToken)));

        drafts.MapPost("/", async (
            DraftGenerationRequest request,
            DraftGenerationRunService service,
            CancellationToken cancellationToken) =>
        {
            DraftGenerationRun run = await service.StartAsync(request, cancellationToken);
            return Results.Accepted($"/api/v1/draft-generation/runs/{run.Id}", run);
        });

        drafts.MapGet("/{id}", async (
            string id,
            DraftService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetRequiredAsync(id, cancellationToken)));

        drafts.MapPut("/{id}", async (
            string id,
            DraftEditRequest request,
            DraftService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.EditAsync(id, request, cancellationToken)));

        drafts.MapPost("/{id}/validate", async (
            string id,
            DraftService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ValidateAsync(id, cancellationToken)));

        drafts.MapPost("/{id}/approve", async (
            string id,
            DraftDecisionRequest request,
            DraftService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ApproveAsync(id, request.ExpectedRevision, cancellationToken)));

        drafts.MapPost("/{id}/reject", async (
            string id,
            DraftDecisionRequest request,
            DraftService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RejectAsync(
                id,
                request.ExpectedRevision,
                request.Reason ?? string.Empty,
                cancellationToken)));

        drafts.MapPost("/{id}/mark-published", async (
            string id,
            MarkPublishedRequest request,
            DraftService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.MarkPublishedAsync(id, request, cancellationToken)));

        drafts.MapGet("/{id}/export", async (
            string id,
            HttpRequest request,
            DraftService service,
            CancellationToken cancellationToken) =>
        {
            string format = request.Query["format"].FirstOrDefault() ?? "plain";
            ExportArtifact artifact = await service.ExportAsync(id, format, cancellationToken);
            return Results.File(
                Encoding.UTF8.GetBytes(artifact.Content),
                artifact.ContentType,
                artifact.FileName);
        });

        RouteGroupBuilder generation = api.MapGroup("/draft-generation/runs");
        generation.MapGet("/", async (
            HttpRequest request,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
            Results.Ok(await workspace.QueryDraftGenerationRunsAsync(
                ApiQueryParser.ParseRun(request),
                cancellationToken)));

        generation.MapGet("/{id}", async (
            string id,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
        {
            DraftGenerationRun run = await workspace.GetDraftGenerationRunAsync(id, cancellationToken)
                ?? throw new ResourceNotFoundException("Draft generation run", id);
            return Results.Ok(run);
        });

        generation.MapPost("/{id}/cancel", async (
            string id,
            DraftGenerationRunService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CancelAsync(id, cancellationToken)));
    }

    private static void MapConfiguration(RouteGroupBuilder api)
    {
        api.MapGet("/topics", async (
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
            Results.Ok(await configuration.GetTopicsAsync(cancellationToken)));

        api.MapPut("/topics", async (
            TopicTaxonomyDocument document,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            await configuration.SaveTopicsAsync(document, cancellationToken);
            return Results.Ok(document);
        });

        api.MapPost("/topics/validate", (TopicTaxonomyDocument document) =>
        {
            List<string> errors = ValidateTopics(document);
            return Results.Ok(new
            {
                valid = errors.Count == 0,
                errors
            });
        });

        api.MapGet("/recipes", async (
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
            Results.Ok(await configuration.GetRecipesAsync(cancellationToken)));

        api.MapGet("/recipes/{id}", async (
            string id,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            ContentRecipesDocument document = await configuration.GetRecipesAsync(cancellationToken);
            ContentRecipe recipe = document.Recipes.FirstOrDefault(candidate =>
                    candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new ResourceNotFoundException("Content recipe", id);
            return Results.Ok(recipe);
        });

        api.MapPut("/recipes/{id}", async (
            string id,
            ContentRecipe recipe,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            if (!id.Equals(recipe.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestValidationException("The route ID and recipe ID must match.");
            }
            ContentRecipesDocument document = await configuration.GetRecipesAsync(cancellationToken);
            if (!document.Recipes.Any(candidate =>
                    candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ResourceNotFoundException("Content recipe", id);
            }
            ContentRecipesDocument updated = document with
            {
                Recipes = document.Recipes.Select(candidate =>
                    candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                        ? recipe
                        : candidate).ToArray()
            };
            await configuration.SaveRecipesAsync(updated, cancellationToken);
            return Results.Ok(recipe);
        });

        api.MapGet("/providers", async (
            HttpRequest request,
            IConfigurationCatalog configuration,
            IAiRouter router,
            CancellationToken cancellationToken) =>
        {
            AiProvidersDocument document = await configuration.GetAiProvidersAsync(cancellationToken);
            bool includeHealth =
                ApiQueryParser.Bool(request, "includeHealth") ||
                ApiQueryParser.Bool(request, "checkHealth");
            if (!includeHealth)
            {
                return Results.Ok(document);
            }

            List<AiProviderHealth> health = new();
            foreach (AiProviderDefinition provider in document.Providers)
            {
                health.Add(await router.CheckHealthAsync(provider.Id, cancellationToken));
            }
            return Results.Ok(new { configuration = document, health });
        });

        api.MapPost("/providers/{id}/test", async (
            string id,
            IAiRouter router,
            CancellationToken cancellationToken) =>
            Results.Ok(await router.CheckHealthAsync(id, cancellationToken)));

        api.MapPut("/providers/{id}", async (
            string id,
            AiProviderDefinition provider,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            if (!id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestValidationException("The route ID and provider ID must match.");
            }
            AiProvidersDocument document = await configuration.GetAiProvidersAsync(cancellationToken);
            if (!document.Providers.Any(candidate =>
                    candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ResourceNotFoundException("AI provider", id);
            }
            AiProvidersDocument updated = document with
            {
                Providers = document.Providers.Select(candidate =>
                    candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                        ? provider
                        : candidate).ToArray()
            };
            await configuration.SaveAiProvidersAsync(updated, cancellationToken);
            return Results.Ok(provider);
        });

        api.MapPut("/providers/routes", async (
            AiRouteDefinition[] routes,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            AiProvidersDocument document = await configuration.GetAiProvidersAsync(cancellationToken);
            AiProvidersDocument updated = document with { Routes = routes };
            await configuration.SaveAiProvidersAsync(updated, cancellationToken);
            return Results.Ok(updated);
        });

        api.MapGet("/settings", async (
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
            Results.Ok(await configuration.GetProfileAsync(cancellationToken)));

        api.MapPut("/settings/profile", async (
            AuthorProfile profile,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            ProfileSettingsDocument document = await configuration.GetProfileAsync(cancellationToken);
            ProfileSettingsDocument updated = document with { Profile = profile };
            await configuration.SaveProfileAsync(updated, cancellationToken);
            return Results.Ok(updated);
        });

        api.MapPut("/settings/schedule", async (
            ScheduleSettings schedule,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            ProfileSettingsDocument document = await configuration.GetProfileAsync(cancellationToken);
            ProfileSettingsDocument updated = document with { Schedule = schedule };
            await configuration.SaveProfileAsync(updated, cancellationToken);
            return Results.Ok(updated);
        });

        api.MapPut("/settings/storage", async (
            StorageSettings storage,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken) =>
        {
            ProfileSettingsDocument document = await configuration.GetProfileAsync(cancellationToken);
            ProfileSettingsDocument updated = document with { Storage = storage };
            await configuration.SaveProfileAsync(updated, cancellationToken);
            return Results.Ok(new
            {
                settings = updated,
                restartRequired = true,
                message = "Restart the API to apply a storage mode or directory change."
            });
        });
    }

    private static void MapRuns(RouteGroupBuilder api)
    {
        api.MapGet("/runs", async (
            HttpRequest request,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
        {
            RunQuery query = ApiQueryParser.ParseRun(request);
            PagedResult<IngestionRun> ingestion = await workspace.QueryIngestionRunsAsync(query, cancellationToken);
            PagedResult<DraftGenerationRun> generation =
                await workspace.QueryDraftGenerationRunsAsync(query, cancellationToken);
            return Results.Ok(new
            {
                ingestion,
                draftGeneration = generation
            });
        });

        api.MapGet("/runs/{id}", async (
            string id,
            IContentWorkspace workspace,
            CancellationToken cancellationToken) =>
        {
            IngestionRun? ingestion = await workspace.GetIngestionRunAsync(id, cancellationToken);
            if (ingestion is not null)
            {
                return Results.Ok(new { type = "ingestion", run = (object)ingestion });
            }
            DraftGenerationRun? generation =
                await workspace.GetDraftGenerationRunAsync(id, cancellationToken);
            if (generation is not null)
            {
                return Results.Ok(new { type = "draft-generation", run = (object)generation });
            }
            throw new ResourceNotFoundException("Run", id);
        });
    }

    private static void MapHealth(WebApplication app, RouteGroupBuilder api)
    {
        static IResult Live(IClock clock) => Results.Ok(new
        {
            status = "healthy",
            service = "DevSignal Studio API",
            time = clock.UtcNow
        });

        static async Task<IResult> Ready(
            IContentWorkspace workspace,
            IConfigurationCatalog configuration,
            CancellationToken cancellationToken)
        {
            bool workspaceReady = await workspace.IsReadyAsync(cancellationToken);
            try
            {
                await configuration.GetTopicsAsync(cancellationToken);
                await configuration.GetSourcesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                return Results.Json(
                    new
                    {
                        status = "unhealthy",
                        workspaceReady,
                        configurationReady = false,
                        message = exception.Message
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return workspaceReady
                ? Results.Ok(new
                {
                    status = "healthy",
                    workspaceReady = true,
                    configurationReady = true
                })
                : Results.Json(
                    new
                    {
                        status = "unhealthy",
                        workspaceReady = false,
                        configurationReady = true
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        app.MapGet("/health/live", Live);
        app.MapGet("/health/ready", Ready);
        api.MapGet("/health/live", Live);
        api.MapGet("/health/ready", Ready);
    }

    private static List<string> ValidateTopics(TopicTaxonomyDocument document)
    {
        List<string> errors = new();
        if (document.SchemaVersion < 1)
        {
            errors.Add("schemaVersion must be at least 1.");
        }
        if (document.Pillars.Count == 0)
        {
            errors.Add("At least one topic pillar is required.");
        }
        string[] duplicates = document.Pillars
            .GroupBy(pillar => pillar.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            errors.Add($"Duplicate pillar IDs: {string.Join(", ", duplicates)}.");
        }
        foreach (TopicPillar pillar in document.Pillars)
        {
            if (string.IsNullOrWhiteSpace(pillar.Id) || string.IsNullOrWhiteSpace(pillar.Name))
            {
                errors.Add("Every pillar requires an ID and name.");
            }
            if (pillar.Keywords.Count == 0)
            {
                errors.Add($"Pillar '{pillar.Id}' requires at least one keyword.");
            }
        }
        return errors;
    }
}
