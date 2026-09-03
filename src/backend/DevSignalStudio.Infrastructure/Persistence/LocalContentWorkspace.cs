using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Application.Models;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Drafting;
using DevSignalStudio.Domain.Runs;
using DevSignalStudio.Infrastructure.Common;

namespace DevSignalStudio.Infrastructure.Persistence;

public sealed class LocalContentWorkspace : IContentWorkspace
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Create();
    private readonly IConfigurationCatalog _configuration;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ContentItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Draft> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IngestionRun> _ingestionRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DraftGenerationRun> _generationRuns = new(StringComparer.OrdinalIgnoreCase);

    private string _workspaceDirectory = string.Empty;
    private bool _persist;
    private int _backupCount = 3;
    private bool _initialized;

    public LocalContentWorkspace(IConfigurationCatalog configuration, IClock clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var profile = await _configuration.GetProfileAsync(cancellationToken);
        _persist = !profile.Storage.Mode.Equals("MemoryOnly", StringComparison.OrdinalIgnoreCase);
        _backupCount = Math.Clamp(profile.Storage.BackupCount, 0, 20);
        string configuredDirectory = string.IsNullOrWhiteSpace(profile.Storage.Directory)
            ? "data"
            : profile.Storage.Directory;
        _workspaceDirectory = Path.IsPathRooted(configuredDirectory)
            ? Path.GetFullPath(configuredDirectory)
            : Path.GetFullPath(Path.Combine(_configuration.RootPath, configuredDirectory));
        EnsureInsideRoot(_configuration.RootPath, _workspaceDirectory);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_workspaceDirectory);
            _items.Clear();
            _drafts.Clear();
            _ingestionRuns.Clear();
            _generationRuns.Clear();

            if (_persist)
            {
                LoadInto(_items, await ReadSnapshotAsync<ContentItem>("items.json", cancellationToken));
                LoadInto(_drafts, await ReadSnapshotAsync<Draft>("drafts.json", cancellationToken));
                LoadInto(_ingestionRuns, await ReadSnapshotAsync<IngestionRun>("ingestion-runs.json", cancellationToken));
                LoadInto(_generationRuns, await ReadSnapshotAsync<DraftGenerationRun>("generation-runs.json", cancellationToken));
                bool changed = RecoverInterruptedRuns();
                if (changed)
                {
                    await PersistIngestionRunsUnlockedAsync(CancellationToken.None);
                    await PersistGenerationRunsUnlockedAsync(CancellationToken.None);
                }
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _initialized && (!_persist || Directory.Exists(_workspaceDirectory));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PagedResult<ContentItem>> QueryItemsAsync(
        ContentQuery query,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            IEnumerable<ContentItem> result = _items.Values;
            if (!string.IsNullOrWhiteSpace(query.Query))
            {
                string term = query.Query.Trim();
                result = result.Where(item =>
                    item.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (item.Summary?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.Content?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    item.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }
            if (!string.IsNullOrWhiteSpace(query.Topic))
            {
                string topic = query.Topic.Trim();
                result = result.Where(item => item.TopicMatches.Any(match =>
                    match.PillarId.Equals(topic, StringComparison.OrdinalIgnoreCase) ||
                    match.PillarName.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                    match.MatchedTerms.Any(term => term.Contains(topic, StringComparison.OrdinalIgnoreCase))));
            }
            if (!string.IsNullOrWhiteSpace(query.SourceId))
            {
                result = result.Where(item => item.SourceId.Equals(query.SourceId, StringComparison.OrdinalIgnoreCase));
            }
            if (query.MinimumScore is double minimumScore)
            {
                result = result.Where(item => item.Score.FinalScore >= minimumScore);
            }
            if (query.Status is ContentItemStatus status)
            {
                result = result.Where(item => item.Status == status);
            }
            if (query.From is DateTimeOffset from)
            {
                result = result.Where(item => (item.PublishedAt ?? item.CollectedAt) >= from);
            }
            if (query.To is DateTimeOffset to)
            {
                result = result.Where(item => (item.PublishedAt ?? item.CollectedAt) <= to);
            }

            result = query.Sort.ToLowerInvariant() switch
            {
                "score-asc" => result.OrderBy(item => item.Score.FinalScore),
                "date-asc" => result.OrderBy(item => item.PublishedAt ?? item.CollectedAt),
                "date-desc" => result.OrderByDescending(item => item.PublishedAt ?? item.CollectedAt),
                "title" => result.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
                _ => result.OrderByDescending(item => item.Score.FinalScore)
                    .ThenByDescending(item => item.PublishedAt ?? item.CollectedAt)
            };

            return Page(result, query.Page, query.PageSize);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ContentItem>> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return _items.Values.OrderBy(item => item.CollectedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ContentItem?> GetItemAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return _items.GetValueOrDefault(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ContentItem?> FindDuplicateAsync(
        string? canonicalUrl,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return _items.Values.FirstOrDefault(item =>
                item.ContentFingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase) ||
                (canonicalUrl is not null && item.CanonicalUrl is not null &&
                 item.CanonicalUrl.Equals(canonicalUrl, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertItemsAsync(IEnumerable<ContentItem> items, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            foreach (ContentItem item in items)
            {
                _items[item.Id] = item;
            }
            await PersistItemsUnlockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SaveItemAsync(ContentItem item, CancellationToken cancellationToken) =>
        UpsertItemsAsync(new[] { item }, cancellationToken);

    public async Task<PagedResult<Draft>> QueryDraftsAsync(
        DraftQuery query,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            IEnumerable<Draft> result = _drafts.Values;
            if (query.Status is DraftStatus status)
            {
                result = result.Where(draft => draft.Status == status);
            }
            if (!string.IsNullOrWhiteSpace(query.Channel))
            {
                result = result.Where(draft => draft.Channel.Equals(query.Channel, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(query.RecipeId))
            {
                result = result.Where(draft => draft.RecipeId.Equals(query.RecipeId, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(query.Topic))
            {
                string topic = query.Topic.Trim();
                HashSet<string> matchingItemIds = _items.Values
                    .Where(item => item.TopicMatches.Any(match =>
                        match.PillarId.Equals(topic, StringComparison.OrdinalIgnoreCase) ||
                        match.PillarName.Contains(topic, StringComparison.OrdinalIgnoreCase)))
                    .Select(item => item.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                result = result.Where(draft => draft.ContentItemIds.Any(matchingItemIds.Contains));
            }

            result = result.OrderByDescending(draft => draft.UpdatedAt);
            return Page(result, query.Page, query.PageSize);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Draft?> GetDraftAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return _drafts.GetValueOrDefault(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveDraftAsync(Draft draft, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            _drafts[draft.Id] = draft;
            await PersistDraftsUnlockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DraftExistsAsync(
        IEnumerable<string> contentItemIds,
        string recipeId,
        CancellationToken cancellationToken)
    {
        HashSet<string> requested = contentItemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return _drafts.Values.Any(draft =>
                draft.RecipeId.Equals(recipeId, StringComparison.OrdinalIgnoreCase) &&
                draft.ContentItemIds.Count == requested.Count &&
                draft.ContentItemIds.All(requested.Contains));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PagedResult<IngestionRun>> QueryIngestionRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            IEnumerable<IngestionRun> result = _ingestionRuns.Values;
            if (query.Status is RunStatus status)
            {
                result = result.Where(run => run.Status == status);
            }
            return Page(result.OrderByDescending(run => run.CreatedAt), query.Page, query.PageSize);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IngestionRun?> GetIngestionRunAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return _ingestionRuns.GetValueOrDefault(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveIngestionRunAsync(IngestionRun run, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            _ingestionRuns[run.Id] = run;
            await PersistIngestionRunsUnlockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PagedResult<DraftGenerationRun>> QueryDraftGenerationRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            IEnumerable<DraftGenerationRun> result = _generationRuns.Values;
            if (query.Status is RunStatus status)
            {
                result = result.Where(run => run.Status == status);
            }
            return Page(result.OrderByDescending(run => run.CreatedAt), query.Page, query.PageSize);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DraftGenerationRun?> GetDraftGenerationRunAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return _generationRuns.GetValueOrDefault(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveDraftGenerationRunAsync(
        DraftGenerationRun run,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            _generationRuns[run.Id] = run;
            await PersistGenerationRunsUnlockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            return new WorkspaceStatistics
            {
                ItemCount = _items.Count,
                CandidateCount = _items.Values.Count(item =>
                    item.Status is ContentItemStatus.Candidate or ContentItemStatus.Promoted),
                DraftCount = _drafts.Count,
                ReviewCount = _drafts.Values.Count(draft => draft.Status == DraftStatus.InReview),
                ApprovedCount = _drafts.Values.Count(draft => draft.Status == DraftStatus.Approved),
                PublishedCount = _drafts.Values.Count(draft => draft.Status == DraftStatus.Published)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<T>> ReadSnapshotAsync<T>(
        string file,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(_workspaceDirectory, file);
        if (!File.Exists(path))
        {
            return Array.Empty<T>();
        }

        try
        {
            SnapshotDocument<T> document = await AtomicJsonFile.ReadAsync<SnapshotDocument<T>>(
                path,
                JsonOptions,
                cancellationToken);
            return document.Items;
        }
        catch (Exception) when (File.Exists(path + ".bak1"))
        {
            SnapshotDocument<T> backup = await AtomicJsonFile.ReadAsync<SnapshotDocument<T>>(
                path + ".bak1",
                JsonOptions,
                cancellationToken);
            return backup.Items;
        }
    }

    private Task PersistItemsUnlockedAsync(CancellationToken cancellationToken) =>
        PersistUnlockedAsync("items.json", _items.Values.OrderBy(item => item.CollectedAt).ToArray(), cancellationToken);

    private Task PersistDraftsUnlockedAsync(CancellationToken cancellationToken) =>
        PersistUnlockedAsync("drafts.json", _drafts.Values.OrderBy(draft => draft.CreatedAt).ToArray(), cancellationToken);

    private Task PersistIngestionRunsUnlockedAsync(CancellationToken cancellationToken) =>
        PersistUnlockedAsync(
            "ingestion-runs.json",
            _ingestionRuns.Values.OrderBy(run => run.CreatedAt).ToArray(),
            cancellationToken);

    private Task PersistGenerationRunsUnlockedAsync(CancellationToken cancellationToken) =>
        PersistUnlockedAsync(
            "generation-runs.json",
            _generationRuns.Values.OrderBy(run => run.CreatedAt).ToArray(),
            cancellationToken);

    private Task PersistUnlockedAsync<T>(
        string file,
        IReadOnlyList<T> values,
        CancellationToken cancellationToken)
    {
        if (!_persist)
        {
            return Task.CompletedTask;
        }

        SnapshotDocument<T> document = new()
        {
            SchemaVersion = 1,
            SavedAt = _clock.UtcNow,
            Items = values
        };
        return AtomicJsonFile.WriteAsync(
            Path.Combine(_workspaceDirectory, file),
            document,
            JsonOptions,
            _backupCount,
            cancellationToken);
    }

    private bool RecoverInterruptedRuns()
    {
        bool changed = false;
        DateTimeOffset now = _clock.UtcNow;
        foreach ((string id, IngestionRun run) in _ingestionRuns.ToArray())
        {
            if (run.Status is RunStatus.Running or RunStatus.Queued)
            {
                _ingestionRuns[id] = run with
                {
                    Status = RunStatus.Failed,
                    CompletedAt = now,
                    Errors = run.Errors.Concat(new[] { "The application stopped before this run completed." }).ToArray()
                };
                changed = true;
            }
        }
        foreach ((string id, DraftGenerationRun run) in _generationRuns.ToArray())
        {
            if (run.Status is RunStatus.Running or RunStatus.Queued)
            {
                _generationRuns[id] = run with
                {
                    Status = RunStatus.Failed,
                    CompletedAt = now,
                    Errors = run.Errors.Concat(new[] { "The application stopped before this run completed." }).ToArray()
                };
                changed = true;
            }
        }
        return changed;
    }

    private static void LoadInto<T>(Dictionary<string, T> target, IReadOnlyList<T> values)
        where T : class
    {
        foreach (T value in values)
        {
            string? id = value switch
            {
                ContentItem item => item.Id,
                Draft draft => draft.Id,
                IngestionRun run => run.Id,
                DraftGenerationRun run => run.Id,
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(id))
            {
                target[id] = value;
            }
        }
    }

    private static PagedResult<T> Page<T>(IEnumerable<T> values, int page, int pageSize)
    {
        int safePage = Math.Max(1, page);
        int safePageSize = Math.Clamp(pageSize, 1, 200);
        T[] all = values.ToArray();
        T[] items = all.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToArray();
        return new PagedResult<T>(items, safePage, safePageSize, all.Length);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The content workspace has not been initialized.");
        }
    }

    private static void EnsureInsideRoot(string root, string candidate)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedCandidate = Path.GetFullPath(candidate);
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedCandidate.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("The workspace directory must remain inside the DevSignal root.");
        }
    }

    private sealed record SnapshotDocument<T>
    {
        public int SchemaVersion { get; init; } = 1;
        public DateTimeOffset SavedAt { get; init; }
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    }
}
