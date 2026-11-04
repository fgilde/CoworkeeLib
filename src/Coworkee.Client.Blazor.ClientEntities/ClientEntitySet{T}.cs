using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Mirrors an OData set as rows of <typeparamref name="T"/>: downloads it in pages, keeps it current with the changes since its version
/// and answers queries locally once complete. Until then, and for queries it cannot answer locally, they go to the server.
/// </summary>
public sealed class ClientEntitySet<T> : ClientEntitySet
{
    private const int FetchChunk = 50;
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

    // one write to the browser storage at a time: a copy saved late must not overwrite a dropped one
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource _local = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LocalTable<T>? _table;

    // a copy restored from the browser storage: it answers once the first update brought it up to date and dropped what the user may no longer see
    private LocalTable<T>? _kept;

    // a download cut off (failed, page closed) goes on from here
    private Download? _download;
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
        if (Mode == ClientEntityMode.Client && _table is { } table && Covers(query))
        {
            try
            {
                return await LocalAsync(table, query, cancellationToken);
            }
            catch (Exception exception) when (LocalQueryNotSupportedException.IsUnanswerable(exception))
            {
                _logger.LogDebug(exception, "{EntitySet} answers {Filter} on the server", EntitySet, query.Filter);
            }
        }

        return await _server.QueryAsync<T>(EntitySet, query, cancellationToken);
    }

    /// <summary>The groups of the rows matching <paramref name="filter"/>, counted locally once the rows are here and by the server until then.</summary>
    public async Task<IReadOnlyList<ODataGroup>> GroupAsync(string? filter, IReadOnlyList<string> by, CancellationToken cancellationToken = default)
    {
        Start();
        if (Mode == ClientEntityMode.Client && _table is { } table)
        {
            try
            {
                return Group(table, filter, by);
            }
            catch (Exception exception) when (LocalQueryNotSupportedException.IsUnanswerable(exception))
            {
                _logger.LogDebug(exception, "{EntitySet} groups {Filter} on the server", EntitySet, filter);
            }
        }

        return await _server.GroupAsync<T>(EntitySet, filter, by, cancellationToken);
    }

    private IReadOnlyList<ODataGroup> Group(LocalTable<T> table, string? filter, IReadOnlyList<string> by)
    {
        var getters = by.Select(path => Getters.GetOrAdd(path, PropertyPath.Getter<T>)).ToList();
        var counts = new Dictionary<GroupKey, long>();
        foreach (var index in View(table, filter, null).Rows)
        {
            var row = table.Row(index);
            var key = new GroupKey([.. getters.Select(get => get(row))]);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return [.. counts.Select(c => new ODataGroup(c.Key.Values, c.Value))];
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<T, object?>> Getters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A LINQ query over the local rows; waits until they are here.</summary>
    public async Task<IReadOnlyList<TResult>> QueryAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> query, CancellationToken cancellationToken = default)
    {
        Start();
        await WhenLocalAsync(cancellationToken);
        return [.. query(Query())];
    }

    public override async Task<(TimeSpan Elapsed, long? Count)> MeasureAsync(ODataQuery query, ClientEntityMode side, CancellationToken cancellationToken = default)
    {
        if (side == ClientEntityMode.Server)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var remote = await _server.QueryAsync<T>(EntitySet, query, cancellationToken);
            return (watch.Elapsed, remote.Count);
        }

        var table = _table ?? throw new InvalidOperationException($"{EntitySet} is not local yet.");
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var local = await LocalAsync(table, query, cancellationToken);
            return (watch.Elapsed, local.Count);
        }
        catch (Exception exception) when (LocalQueryNotSupportedException.IsUnanswerable(exception))
        {
            throw new LocalQueryNotSupportedException(exception.Message, exception);
        }
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

    /// <summary>The server answered for another user, tenant or group selection than this copy's: the copy is dropped here, the storage keeps it.</summary>
    internal event Action? Rescoped;

    // a disposed set holds no rows: whoever kept it must not see the copy of a scope that ended
    public override async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null)
        {
            await _running;
        }

        _table = null;
        _kept = null;
        _download = null;
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
            // every value of the set: the copy counts on all its rows, a sample's options would miss some
            return _facets = (await _server.QueryAsync<T>(EntitySet, new ODataQuery { Top = 0, Count = false, Facets = true, FacetsOfAllRows = true, Expand = Expand }, _stop.Token)).Facets;
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

            if (SyncPaused)
            {
                return;
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
        catch (RescopedException)
        {
            // another user, tenant or group selection: nothing of this copy may answer or be saved any more; ClientEntities starts over
            Drop();
            Publish(ClientEntityStatus.Initial with { Revision = Status.Revision + 1 });
            Rescoped?.Invoke();
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

    private async Task<ClientEntityChanges> ChangesAsync(DateTimeOffset? since, int? skip, CancellationToken cancellationToken)
    {
        var changes = await _api.ChangesAsync(EntitySet, since, skip, cancellationToken);
        return changes.Partition is { } partition && partition != _partition ? throw new RescopedException() : changes;
    }

    // keyset pages (Id gt the last row) in the order of the ids: each round is kept in the browser storage, so a reload or a failure goes on from
    // the last round instead of from zero; the rows changed meanwhile come with the next update, which starts at the version taken before the download
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        var start = await ChangesAsync(null, null, cancellationToken);
        if (start.Count > _info.MaxRows)
        {
            _download = null;
            await DropPagesAsync();
            Publish(Status with { State = ClientEntitySyncState.TooLarge, Total = start.Count, Error = null });
            _local.TrySetException(new InvalidOperationException($"{EntitySet} has more than {_info.MaxRows} rows and stays on the server."));
            return;
        }

        if (_download is null)
        {
            await DropPagesAsync();
            _download = new Download(start.Version);
        }

        var download = _download;
        var batch = _info.BatchSize;
        var parallel = Math.Max(1, _info.ParallelPages);
        Publish(Status with { State = ClientEntitySyncState.Downloading, Loaded = download.Rows.Count, Total = Math.Max(start.Count, download.Rows.Count), Error = null });
        while (true)
        {
            var after = download.Cursor is { } cursor ? $"Id gt {cursor}" : null;
            var pages = await Task.WhenAll(Enumerable.Range(0, parallel).Select(page =>
                RowsAsync(new ODataQuery { Filter = after, OrderBy = "Id", Top = batch, Skip = page * batch, Count = false }, cancellationToken)));
            var items = pages.SelectMany(p => p.Items).ToList();
            foreach (var item in items)
            {
                download.Rows[IdOf!(item)] = item;
            }

            if (items.Count > 0)
            {
                download.Cursor = IdOf!(items[^1]);
                await SavePageAsync(download, items);
            }

            Publish(Status with { Loaded = download.Rows.Count, Total = Math.Max(start.Count, download.Rows.Count) });
            if (pages[^1].Items.Count < batch)
            {
                break;
            }

            if (SyncPaused)
            {
                // the download keeps its place (_download): resumed, it goes on from the last round
                Publish(Status with { State = ClientEntitySyncState.Idle });
                return;
            }

            await Pause();
        }

        _download = null;
        Commit(LocalTable<T>.Of(download.Rows.Values), download.Version, changed: true);
        await SaveAsync();
        await DropPagesAsync();
    }

    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        Publish(Status with { State = ClientEntitySyncState.Updating, Error = null });
        var changes = await ChangesAsync(_version, 0, cancellationToken);
        if (changes.Reset)
        {
            await DownloadAsync(cancellationToken);
            return;
        }

        // many changes (a bulk update on the server) come in pages instead of a download from zero
        List<Guid> changedIds = [.. changes.Changed];
        for (var page = changes; page.More;)
        {
            page = await ChangesAsync(_version, changedIds.Count, cancellationToken);
            changedIds.AddRange(page.Changed);
        }

        var table = _table ?? _kept!;
        if (changedIds.Count == 0 && changes.Deleted.Count == 0 && changes.Count == table.Count)
        {
            Commit(table, changes.Version, changed: false);
            return;
        }

        var rows = table.All().ToDictionary(IdOf!);
        var changed = changes.Deleted.Count(rows.Remove) > 0;
        changed |= await FetchIntoAsync(rows, changedIds, cancellationToken);
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
        foreach (var round in ids.Chunk(FetchChunk).Chunk(Math.Max(1, _info.ParallelPages)))
        {
            var pages = await Task.WhenAll(round.Select(chunk =>
                RowsAsync(new ODataQuery { Filter = $"Id in ({string.Join(",", chunk)})", Top = chunk.Length, Count = false }, cancellationToken)));
            foreach (var item in pages.SelectMany(p => p.Items))
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
        _kept = null;
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

    private void Drop()
    {
        _table = null;
        _kept = null;
        _download = null;
        _version = null;
        _facets = null;
        var waiting = _local;
        _local = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.TrySetException(new InvalidOperationException($"The copy of {EntitySet} belonged to another user or scope."));
    }

    private async Task ForgetAsync()
    {
        _table = null;
        _kept = null;
        _download = null;
        _version = null;
        _facets = null;
        if (_local.Task.IsCompleted)
        {
            _local = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        await WriteAsync(async () =>
        {
            await _storage.SaveAsync(StorageKey, []);
            await _storage.SaveAsync(ProgressKey, []);
            await _storage.DeleteAsync(PagesPrefix);
        }, "Dropping the kept copy of {EntitySet} failed");
    }

    private string StorageKey => $"{_partition}/{EntitySet}/{Expand}";

    // the rounds of an unfinished download: "#progress" says how many there are, up to where and from which version
    private string PagesPrefix => StorageKey + "#";

    private string ProgressKey => PagesPrefix + "progress";

    private string PageKey(int page) => PagesPrefix + page.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task SavePageAsync(Download download, IReadOnlyList<T> items)
    {
        if (!download.Kept)
        {
            return;
        }

        var data = await PackAsync(LocalTable<T>.Of(items), download.Version);
        var progress = JsonSerializer.SerializeToUtf8Bytes(new Progress(download.Version, download.Cursor, download.Pages + 1), Json);
        // a page that was not kept would leave a gap behind the next progress: this download keeps no further pages, a reload goes on from the last kept one
        download.Kept = await WriteAsync(async () =>
        {
            await _storage.SaveAsync(PageKey(download.Pages), data);
            await _storage.SaveAsync(ProgressKey, progress);
        }, "Keeping a page of {EntitySet} failed");
        if (download.Kept)
        {
            download.Pages++;
        }
    }

    private Task DropPagesAsync() => WriteAsync(async () =>
    {
        await _storage.SaveAsync(ProgressKey, []);
        await _storage.DeleteAsync(PagesPrefix);
    }, "Dropping the pages of {EntitySet} failed");

    // one write to the browser storage at a time; a full or blocked storage costs the next visit a download, nothing more
    private async Task<bool> WriteAsync(Func<Task> write, string failure)
    {
        await _writes.WaitAsync();
        try
        {
            await write();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, failure, EntitySet);
            return false;
        }
        finally
        {
            _writes.Release();
        }
    }

    private async Task SaveAsync()
    {
        if (_table is not { } table || _version is not { } version)
        {
            return;
        }

        try
        {
            var data = await PackAsync(table, version);
            // the copy was replaced or dropped while it was being packed
            await WriteAsync(() => _table == table ? _storage.SaveAsync(StorageKey, data) : Task.CompletedTask, "Keeping {EntitySet} for the next visit failed");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Keeping {EntitySet} for the next visit failed", EntitySet);
        }
    }

    // rows whose type splits into columns are kept as columns, which come back without parsing; others as JSON
    private static async Task<byte[]> PackAsync(LocalTable<T> table, DateTimeOffset version)
    {
        using var buffer = new MemoryStream();
        await using (var zip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            if (LocalTable<T>.Shape is not null)
            {
                await table.WriteAsync(zip, version, Pause);
            }
            else
            {
                await JsonSerializer.SerializeAsync(zip, new Snapshot(version, Schema, table.All()), Json);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Null when the data was written for another row type.</summary>
    private static async Task<(LocalTable<T> Table, DateTimeOffset Version)?> UnpackAsync(byte[] data)
    {
        using var plain = new MemoryStream();
        await using (var zip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
        {
            // in slices with a pause between them, so unpacking a large copy does not hold the page
            var slice = new byte[1 << 20];
            int read;
            while ((read = await zip.ReadAsync(slice)) > 0)
            {
                plain.Write(slice, 0, read);
                await Pause();
            }
        }

        plain.Position = 0;
        if (LocalTable<T>.IsColumns(plain.GetBuffer().AsSpan(0, (int)plain.Length)))
        {
            return await LocalTable<T>.ReadAsync(plain, Pause);
        }

        return await JsonSerializer.DeserializeAsync<Snapshot>(plain, Json) is { } snapshot && snapshot.Schema == Schema
            ? (LocalTable<T>.Of(snapshot.Items), snapshot.Version)
            : null;
    }

    // a complete copy waits for its first update (see _kept); without one, the pages of a cut off download are picked up
    private async Task RestoreAsync()
    {
        try
        {
            if (await _storage.LoadAsync(StorageKey) is { Length: > 0 } data && await UnpackAsync(data) is { } kept)
            {
                _kept = kept.Table;
                _version = kept.Version;
                return;
            }

            if (await _storage.LoadAsync(ProgressKey) is not { Length: > 0 } saved || JsonSerializer.Deserialize<Progress>(saved, Json) is not { } progress)
            {
                return;
            }

            var download = new Download(progress.Version) { Cursor = progress.Cursor, Pages = progress.Pages };
            for (var page = 0; page < progress.Pages; page++)
            {
                if (await _storage.LoadAsync(PageKey(page)) is not { Length: > 0 } part || await UnpackAsync(part) is not { } rows)
                {
                    return;
                }

                foreach (var row in rows.Table.All())
                {
                    download.Rows[IdOf!(row)] = row;
                }
            }

            _download = download;
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

    private sealed class GroupKey(object?[] values) : IEquatable<GroupKey>
    {
        public object?[] Values => values;

        public bool Equals(GroupKey? other) => other is not null && values.SequenceEqual(other.Values);

        public override bool Equals(object? obj) => Equals(obj as GroupKey);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }

    private sealed record Snapshot(DateTimeOffset Version, string Schema, T[] Items);

    private sealed record Progress(DateTimeOffset Version, Guid? Cursor, int Pages);

    private sealed class Download(DateTimeOffset version)
    {
        public DateTimeOffset Version { get; } = version;

        public Dictionary<Guid, T> Rows { get; } = [];

        public Guid? Cursor { get; set; }

        public int Pages { get; set; }

        public bool Kept { get; set; } = true;
    }

    private sealed class RescopedException : Exception;
}
