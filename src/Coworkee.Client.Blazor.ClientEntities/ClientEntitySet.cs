using System.Collections.Concurrent;
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
    private const int MaxCachedFilters = 100;

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
    private readonly ConcurrentDictionary<string, Func<T, bool>> _filters = new(StringComparer.Ordinal);
    private TaskCompletionSource _local = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Dictionary<Guid, T> _rows = [];
    private T[]? _items;
    private DateTimeOffset? _version;
    private IReadOnlyList<FacetGroupDto>? _facets;
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

    public override string EntitySet => _info.EntitySet;

    /// <summary>The navigation properties each row carries, as in $expand.</summary>
    public string? Expand { get; }

    public bool IsLocal => _items is not null;

    /// <summary>All local rows; empty until the first download completed.</summary>
    public IReadOnlyList<T> Items => _items ?? [];

    /// <summary>The local rows for LINQ or a QuickGrid's Items; empty until the first download completed.</summary>
    public IQueryable<T> Query() => Items.AsQueryable();

    /// <summary>Completes when the rows are local; fails if the set stays on the server or the download failed.</summary>
    public Task WhenLocalAsync(CancellationToken cancellationToken = default) => _local.Task.WaitAsync(cancellationToken);

    /// <summary>An OData query, answered locally once the rows are here and by the server until then.</summary>
    public async Task<ODataPage<T>> QueryAsync(ODataQuery query, CancellationToken cancellationToken = default)
    {
        Start();
        if (_items is { } items && Covers(query))
        {
            try
            {
                return await LocalAsync(items, query, cancellationToken);
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
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
        string.IsNullOrWhiteSpace(query.Search)
        && (string.IsNullOrWhiteSpace(query.Expand) || Names(query.Expand).All(Names(Expand).Contains));

    private static HashSet<string> Names(string? expand) =>
        new((expand ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private async Task<ODataPage<T>> LocalAsync(T[] items, ODataQuery query, CancellationToken cancellationToken)
    {
        var rows = string.IsNullOrWhiteSpace(query.Filter) ? items : items.Where(Compile(query.Filter)).ToArray();
        var facets = query.Facets ? LocalFacets.Count(await FacetsAsync(cancellationToken), rows, Compile) : [];
        var page = ODataLinq.OrderBy(rows, query.OrderBy).Skip(query.Skip ?? 0);
        return new ODataPage<T>([.. query.Top is { } top ? page.Take(top) : page], rows.Length, facets);
    }

    private Func<T, bool> Compile(string filter)
    {
        if (_filters.Count > MaxCachedFilters)
        {
            // ponytail: every search term is a new filter; dropping the cache is cheaper than tracking use
            _filters.Clear();
        }

        return _filters.GetOrAdd(filter, f => ODataLinq.Filter<T>(f).Compile());
    }

    // the server knows which facets exist and their labels; locally only the counts change
    private async Task<IReadOnlyList<FacetGroupDto>> FacetsAsync(CancellationToken cancellationToken) =>
        _facets ??= (await _server.QueryAsync<T>(EntitySet, new ODataQuery { Top = 0, Count = false, Facets = true, Expand = Expand }, cancellationToken)).Facets;

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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Syncing {EntitySet} failed", EntitySet);
            Publish(Status with { State = ClientEntitySyncState.Failed, Error = exception.Message });
            if (_items is null)
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
        Publish(Status with { State = ClientEntitySyncState.Downloading, Loaded = 0, Total = start.Count, Error = null });
        for (var skip = 0; ; skip += _info.BatchSize)
        {
            var page = await _server.QueryAsync<T>(EntitySet, new ODataQuery { OrderBy = "Id", Top = _info.BatchSize, Skip = skip, Expand = Expand, Count = false }, cancellationToken);
            foreach (var item in page.Items)
            {
                rows[IdOf!(item)] = item;
            }

            Publish(Status with { Loaded = rows.Count, Total = Math.Max(start.Count, rows.Count) });
            if (page.Items.Count < _info.BatchSize)
            {
                break;
            }
        }

        // rows changed meanwhile come with the next update, which starts at the version taken before the download
        Commit(rows, start.Version, changed: true);
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

        var rows = new Dictionary<Guid, T>(_rows);
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

        Commit(rows, changes.Version, changed);
        if (changed)
        {
            await SaveAsync();
        }
    }

    private async Task<bool> FetchIntoAsync(Dictionary<Guid, T> rows, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        foreach (var chunk in ids.Chunk(FetchChunk))
        {
            var page = await _server.QueryAsync<T>(EntitySet, new ODataQuery { Filter = $"Id in ({string.Join(",", chunk)})", Top = chunk.Length, Expand = Expand, Count = false }, cancellationToken);
            foreach (var item in page.Items)
            {
                rows[IdOf!(item)] = item;
            }
        }

        return ids.Count > 0;
    }

    private void Commit(Dictionary<Guid, T> rows, DateTimeOffset version, bool changed)
    {
        _rows = rows;
        _version = version;
        var revision = Status.Revision;
        if (changed || _items is null)
        {
            _items = [.. rows.Values];
            _facets = null;
            revision++;
        }

        Publish(new ClientEntityStatus(ClientEntitySyncState.Idle, true, rows.Count, rows.Count, version, null, revision));
        _local.TrySetResult();
    }

    private string StorageKey => $"{_partition}/{EntitySet}/{Expand}";

    private async Task SaveAsync()
    {
        try
        {
            using var buffer = new MemoryStream();
            await using (var zip = new GZipStream(buffer, CompressionLevel.Fastest))
            {
                await JsonSerializer.SerializeAsync(zip, new Snapshot(_version!.Value, Schema, _items!), Json);
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

            await using var zip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            if (await JsonSerializer.DeserializeAsync<Snapshot>(zip, Json) is { } snapshot && snapshot.Schema == Schema)
            {
                Commit(snapshot.Items.ToDictionary(IdOf!), snapshot.Version, changed: true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Restoring {EntitySet} failed, it is downloaded again", EntitySet);
        }
    }

    private sealed record Snapshot(DateTimeOffset Version, string Schema, T[] Items);
}
