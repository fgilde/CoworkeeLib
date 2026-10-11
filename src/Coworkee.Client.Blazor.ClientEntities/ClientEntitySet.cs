using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>The local copy of one OData set, whatever its row type.</summary>
public abstract class ClientEntitySet : IAsyncDisposable
{
    public abstract string EntitySet { get; }

    public ClientEntityStatus Status { get; private set; } = ClientEntityStatus.Initial;

    /// <summary>The status or the rows changed.</summary>
    public event Action<ClientEntitySet>? Changed;

    /// <summary>Asks the server for changes now instead of at the next interval.</summary>
    public abstract Task RefreshAsync(CancellationToken cancellationToken = default);

    public abstract ValueTask DisposeAsync();

    /// <summary>Something changed on the server: refresh soon, several nudges in a row refresh once.</summary>
    internal abstract void Nudge();

    protected void Publish(ClientEntityStatus status)
    {
        Status = status;
        Changed?.Invoke(this);
    }
}

/// <summary>
/// Mirrors an OData set as rows of <typeparamref name="T"/>: downloads it in pages, keeps it current with the changes since its version
/// and answers queries locally once complete. Until then, and for queries it cannot answer locally, they go to the server.
/// </summary>
public sealed class ClientEntitySet<T> : ClientEntitySet
{
    private const int FetchChunk = 50;
    private const int ParallelPages = 2;
    private const int MaxViews = 16;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Func<T, Guid>? IdOf = typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance) is { PropertyType: var type, GetMethod: { } getter } && type == typeof(Guid)
        ? getter.CreateDelegate<Func<T, Guid>>()
        : null;

    private static readonly string Schema = typeof(T).FullName + ":" + string.Join(",", typeof(T).GetProperties().Select(p => p.Name + " " + p.PropertyType.Name));

    private readonly ClientEntitySetInfo _info;
    private readonly string _partition;
    private readonly IODataClient _server;
    private readonly ClientEntityApi _api;
    private readonly IClientEntityStorage _storage;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource _local = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LocalTable<T>? _table;
    private DateTimeOffset? _version;
    private IReadOnlyList<FacetGroupDto>? _facets;
    private Task<IReadOnlyList<FacetGroupDto>>? _facetsLoading;
    private bool _facetsStale;
    private readonly Dictionary<(string? Filter, string? OrderBy), LocalView> _views = [];
    private bool _selectRejected;
    private bool _restored;
    private int _nudged;
    private Task? _running;

    internal ClientEntitySet(ClientEntitySetInfo info, string? expand, string partition, IODataClient server, ClientEntityApi api, IClientEntityStorage storage, ILogger logger)
    {
        _info = info;
        Expand = string.IsNullOrWhiteSpace(expand) ? null : expand;
        _partition = partition;
        _server = server;
        _api = api;
        _storage = storage;
        _logger = logger;
    }

    internal static bool CanMirror => IdOf is not null;

    /// <summary>Gives the browser a turn between long steps, so the page keeps rendering and the server answers meanwhile.</summary>
    internal static Func<Task> Pause { get; set; } = () => Task.Delay(1);

    public override string EntitySet => _info.EntitySet;

    /// <summary>The navigation properties each row carries, as in $expand.</summary>
    public string? Expand { get; }

    public bool IsLocal => _table is not null;

    /// <summary>All local rows; empty until the first download completed. Rows kept as columns become objects here.</summary>
    public IReadOnlyList<T> Items => _table?.All() ?? [];

    /// <summary>The local rows for LINQ or a QuickGrid's Items; empty until the first download completed.</summary>
    public IQueryable<T> Query() => Items.AsQueryable();

    /// <summary>Completes when the rows are local; fails if the set stays on the server or the download failed.</summary>
    public Task WhenLocalAsync(CancellationToken cancellationToken = default) => _local.Task.WaitAsync(cancellationToken);

    /// <summary>An OData query, answered locally once the rows are here and by the server until then.</summary>
    public async Task<ODataPage<T>> QueryAsync(ODataQuery query, CancellationToken cancellationToken = default)
    {
        Start();
        if (_table is { } table && Covers(query))
        {
            try
            {
                return await LocalAsync(table, query, cancellationToken);
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException or FormatException or InvalidCastException)
            {
                _logger.LogDebug(exception, "{EntitySet} answers {Filter} on the server", EntitySet, query.Filter);
            }
        }

        return await _server.QueryAsync<T>(EntitySet, query, cancellationToken);
    }

    /// <summary>A LINQ query over the local rows; waits until they are here.</summary>
    public async Task<IReadOnlyList<TResult>> QueryAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> query, CancellationToken cancellationToken = default)
    {
        Start();
        await WhenLocalAsync(cancellationToken);
        return [.. query(Query())];
    }

    public override Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Start();
        return SyncAsync(cancellationToken);
    }

    internal void Start() => _running ??= RunAsync();

    internal override void Nudge()
    {
        if (Interlocked.Exchange(ref _nudged, 1) == 0)
        {
            _ = NudgedAsync();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null)
        {
            await _running;
        }

        _stop.Dispose();
    }

    private async Task NudgedAsync()
    {
        try
        {
            await Task.Delay(300, _stop.Token);
            Interlocked.Exchange(ref _nudged, 0);
            await SyncAsync(_stop.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await SyncAsync(_stop.Token);
            if (_info.RefreshInterval <= TimeSpan.Zero)
            {
                return;
            }

            using var timer = new PeriodicTimer(_info.RefreshInterval);
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                await SyncAsync(_stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool Covers(ODataQuery query) =>
        string.IsNullOrWhiteSpace(query.Search) && string.IsNullOrWhiteSpace(query.Select)
        && (string.IsNullOrWhiteSpace(query.Expand) || Names(query.Expand).All(Names(Expand).Contains));

    private static HashSet<string> Names(string? expand) =>
        new((expand ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private async Task<ODataPage<T>> LocalAsync(LocalTable<T> table, ODataQuery query, CancellationToken cancellationToken)
    {
        var view = View(table, query.Filter, query.OrderBy);
        var facets = query.Facets ? view.Facets ??= LocalFacets.Count(await FacetsAsync(cancellationToken), table, view.Rows, query.FacetContext) : [];
        var skip = Math.Min(query.Skip ?? 0, view.Rows.Length);
        var items = new T[Math.Min(query.Top ?? int.MaxValue, view.Rows.Length - skip)];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = table.Row(view.Rows[skip + i]);
        }

        return new ODataPage<T>(items, view.Rows.Length, facets);
    }

    // paging, grouping, facet counts and going back to an earlier filter reuse the rows found before
    private LocalView View(LocalTable<T> table, string? filter, string? orderBy)
    {
        lock (_views)
        {
            if (_views.TryGetValue((filter, orderBy), out var known) && known.Table == table)
            {
                return known;
            }

            if (_views.Count >= MaxViews || _views.Values.Any(v => v.Table != table))
            {
                _views.Clear();
            }

            return _views[(filter, orderBy)] = new LocalView(table, table.Find(filter, orderBy));
        }
    }

    // the server knows which facets exist and their labels; locally only the counts change, so changed rows refresh them in the background
    private async Task<IReadOnlyList<FacetGroupDto>> FacetsAsync(CancellationToken cancellationToken)
    {
        if (_facets is not { } known)
        {
            return await (_facetsLoading ??= LoadFacetsAsync()).WaitAsync(cancellationToken);
        }

        if (_facetsStale)
        {
            _facetsStale = false;
            _facetsLoading ??= LoadFacetsAsync();
        }

        return known;
    }

    private async Task<IReadOnlyList<FacetGroupDto>> LoadFacetsAsync()
    {
        try
        {
            return _facets = (await _server.QueryAsync<T>(EntitySet, new ODataQuery { Top = 0, Count = false, Facets = true, Expand = Expand }, _stop.Token)).Facets;
        }
        finally
        {
            _facetsLoading = null;
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_restored)
            {
                _restored = true;
                await RestoreAsync();
            }

            if (_version is null)
            {
                await DownloadAsync(cancellationToken);
            }
            else
            {
                await UpdateAsync(cancellationToken);
            }
        }
        catch (Api.ApiException exception) when (exception.Status is 403 or 404)
        {
            // the user may no longer read the set, or it is no longer mirrored: the copy goes
            await ForgetAsync();
            Publish(ClientEntityStatus.Initial with { State = ClientEntitySyncState.Failed, Error = exception.Message, Revision = Status.Revision + 1 });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Syncing {EntitySet} failed", EntitySet);
            Publish(Status with { State = ClientEntitySyncState.Failed, Error = exception.Message });
            if (_table is null)
            {
                var waiting = _local;
                _local = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waiting.TrySetException(exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        var start = await _api.ChangesAsync(EntitySet, null, cancellationToken);
        if (start.Count > _info.MaxRows)
        {
            Publish(Status with { State = ClientEntitySyncState.TooLarge, Total = start.Count, Error = null });
            _local.TrySetException(new InvalidOperationException($"{EntitySet} has more than {_info.MaxRows} rows and stays on the server."));
            return;
        }

        var rows = new Dictionary<Guid, T>((int)start.Count);
        var batch = _info.BatchSize;
        var known = (int)((start.Count + batch - 1) / batch);
        Publish(Status with { State = ClientEntitySyncState.Downloading, Loaded = 0, Total = start.Count, Error = null });
        for (var first = 0; ; first += ParallelPages)
        {
            // pages known from the count load side by side; rows added meanwhile follow one page at a time
            var pages = await Task.WhenAll(Enumerable.Range(first, first < known ? Math.Min(ParallelPages, known - first) : 1).Select(page =>
                RowsAsync(new ODataQuery { OrderBy = "Id", Top = batch, Skip = page * batch, Count = false }, cancellationToken)));
            foreach (var item in pages.SelectMany(p => p.Items))
            {
                rows[IdOf!(item)] = item;
            }

            Publish(Status with { Loaded = rows.Count, Total = Math.Max(start.Count, rows.Count) });
            if (pages[^1].Items.Count < batch)
            {
                break;
            }
        }

        // rows changed meanwhile come with the next update, which starts at the version taken before the download
        Commit(LocalTable<T>.Of(rows.Values), start.Version, changed: true);
        await SaveAsync();
    }

    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        Publish(Status with { State = ClientEntitySyncState.Updating, Error = null });
        var changes = await _api.ChangesAsync(EntitySet, _version, cancellationToken);
        if (changes.Reset)
        {
            await DownloadAsync(cancellationToken);
            return;
        }

        var table = _table!;
        if (changes.Changed.Count == 0 && changes.Deleted.Count == 0 && changes.Count == table.Count)
        {
            Commit(table, changes.Version, changed: false);
            return;
        }

        var rows = table.All().ToDictionary(IdOf!);
        var changed = changes.Deleted.Count(rows.Remove) > 0;
        changed |= await FetchIntoAsync(rows, changes.Changed, cancellationToken);
        if (rows.Count != changes.Count)
        {
            // rows vanished unseen: hard deleted, moved to another tenant or no longer visible to this user
            var visible = (await _api.IdsAsync(EntitySet, cancellationToken)).ToHashSet();
            foreach (var id in rows.Keys.Where(id => !visible.Contains(id)).ToList())
            {
                changed |= rows.Remove(id);
            }

            changed |= await FetchIntoAsync(rows, [.. visible.Where(id => !rows.ContainsKey(id))], cancellationToken);
        }

        Commit(changed ? LocalTable<T>.Of(rows.Values) : table, changes.Version, changed);
        if (changed)
        {
            await SaveAsync();
        }
    }

    private async Task<bool> FetchIntoAsync(Dictionary<Guid, T> rows, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        foreach (var chunk in ids.Chunk(FetchChunk))
        {
            var page = await RowsAsync(new ODataQuery { Filter = $"Id in ({string.Join(",", chunk)})", Top = chunk.Length, Count = false }, cancellationToken);
            foreach (var item in page.Items)
            {
                rows[IdOf!(item)] = item;
            }
        }

        return ids.Count > 0;
    }

    // only the properties of the row type travel; a server that rejects them sends whole rows
    private async Task<ODataPage<T>> RowsAsync(ODataQuery query, CancellationToken cancellationToken)
    {
        if (_selectRejected || LocalTable<T>.Shape is not { } shape)
        {
            return await _server.QueryAsync<T>(EntitySet, query with { Expand = Expand }, cancellationToken);
        }

        var (select, expand) = shape.Query(Expand);
        try
        {
            return await _server.QueryAsync<T>(EntitySet, query with { Select = select, Expand = expand }, cancellationToken);
        }
        catch (Api.ApiException exception) when (exception.Status == 400)
        {
            _logger.LogDebug("{EntitySet} does not take the select of {Type}, rows come whole", EntitySet, typeof(T).Name);
            _selectRejected = true;
            return await RowsAsync(query, cancellationToken);
        }
    }

    private void Commit(LocalTable<T> table, DateTimeOffset version, bool changed)
    {
        _version = version;
        var revision = Status.Revision;
        if (changed || _table is null)
        {
            _table = table;
            _facetsStale = true;
            revision++;
            _ = IndexAsync(table);
        }

        Publish(new ClientEntityStatus(ClientEntitySyncState.Idle, true, table.Count, table.Count, version, null, revision));
        _local.TrySetResult();
    }

    // like database indexes: each column sorts once, between columns the page renders and queries run; the sort orders are kept with the copy
    private async Task IndexAsync(LocalTable<T> table)
    {
        try
        {
            var sorted = false;
            foreach (var path in table.Indexable)
            {
                await Pause();
                if (_table != table || _stop.IsCancellationRequested)
                {
                    return;
                }

                sorted |= table.Column(path).Index();
            }

            if (sorted && _table == table)
            {
                await SaveAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Indexing {EntitySet} stopped", EntitySet);
        }
    }

    private async Task ForgetAsync()
    {
        _table = null;
        _version = null;
        _facets = null;
        if (_local.Task.IsCompleted)
        {
            _local = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            await _storage.SaveAsync(StorageKey, []);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Dropping the kept copy of {EntitySet} failed", EntitySet);
        }
    }

    private string StorageKey => $"{_partition}/{EntitySet}/{Expand}";

    // rows whose type splits into columns are kept as columns, which come back without parsing; others as JSON
    private async Task SaveAsync()
    {
        try
        {
            using var buffer = new MemoryStream();
            await using (var zip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                if (LocalTable<T>.Shape is not null)
                {
                    _table!.Write(zip, _version!.Value);
                }
                else
                {
                    await JsonSerializer.SerializeAsync(zip, new Snapshot(_version!.Value, Schema, _table!.All()), Json);
                }
            }

            await _storage.SaveAsync(StorageKey, buffer.ToArray());
        }
        catch (Exception exception)
        {
            // a full or blocked browser storage costs the next visit a download, nothing more
            _logger.LogWarning(exception, "Keeping {EntitySet} for the next visit failed", EntitySet);
        }
    }

    private async Task RestoreAsync()
    {
        try
        {
            if (await _storage.LoadAsync(StorageKey) is not { Length: > 0 } data)
            {
                return;
            }

            using var plain = new MemoryStream();
            await using (var zip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            {
                await zip.CopyToAsync(plain);
            }

            plain.Position = 0;
            if (LocalTable<T>.IsColumns(plain.GetBuffer().AsSpan(0, (int)plain.Length)))
            {
                if (await LocalTable<T>.ReadAsync(plain, Pause) is { } restored)
                {
                    Commit(restored.Table, restored.Version, changed: true);
                }
            }
            else if (await JsonSerializer.DeserializeAsync<Snapshot>(plain, Json) is { } snapshot && snapshot.Schema == Schema)
            {
                Commit(LocalTable<T>.Of(snapshot.Items), snapshot.Version, changed: true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Restoring {EntitySet} failed, it is downloaded again", EntitySet);
        }
    }

    private sealed record LocalView(LocalTable<T> Table, int[] Rows)
    {
        public IReadOnlyList<FacetGroupDto>? Facets { get; set; }
    }

    private sealed record Snapshot(DateTimeOffset Version, string Schema, T[] Items);
}
