using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public enum Kind
{
    Hand,
    Power,
}

public sealed record Brand(Guid Id, string Name);

public sealed record Gadget(Guid Id, string Name, string Category, decimal Price, Brand? Brand = null, Kind Kind = Kind.Hand, DateTimeOffset Created = default, string? Note = null);

/// <summary>The OData set and the sync feed of a server, over rows the test changes.</summary>
internal sealed class FakeServer : HttpMessageHandler, IODataClient
{
    public List<Gadget> Rows { get; } = [];

    public List<ODataQuery> Queries { get; } = [];

    public ClientEntityChanges? NextChanges { get; set; }

    /// <summary>Holds the sync feed back, so the test sees the set before its rows arrived.</summary>
    public Task Feed { get; set; } = Task.CompletedTask;

    public int MaxRows { get; set; } = 1000;

    public bool Forbidden { get; set; }

    public IReadOnlyList<FacetGroupDto> Facets { get; set; } = [];

    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-10T10:00:00Z");

    public string? RealtimeTopic { get; set; }

    public int BatchSize { get; set; } = 2;

    public int ParallelPages { get; set; } = 1;

    public string? Expand { get; set; }

    /// <summary>The fields the server's query_data returns of the set; null: a server that does not name them.</summary>
    public IReadOnlyList<string>? Fields { get; set; }

    public ClientEntitySetInfo Info => new("Gadgets", BatchSize, MaxRows, TimeSpan.Zero, RealtimeTopic, Expand, ParallelPages, Fields);

    public ClientEntitySet<Gadget> Set(IClientEntityStorage? storage = null, string partition = "p1", IClientEntityEngine? engine = null) =>
        new(Info, "Brand", partition, this, Api, storage ?? new NoClientEntityStorage(), NullLogger.Instance, engine);

    public ClientEntityApi Api => new(new HttpClient(this) { BaseAddress = new Uri("http://app/") });

    public ClientEntities Entities(IClientEntityStorage? storage = null, IClientEntityEngine? engine = null) =>
        new(Api, this, storage ?? new NoClientEntityStorage(), null, new ClientEntitySignal(), NullLogger<ClientEntities>.Instance, engine: engine);

    public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
    {
        await RowsGate;
        Queries.Add(query);
        if (FailRowsAfter is { } fail && Queries.Count(q => q.OrderBy == "Id") > fail)
        {
            throw new HttpRequestException("the server went away");
        }

        // the keyset of the download: ids in the order the rows are sorted by (here .NET's, on SQL Server the database's)
        var rows = string.IsNullOrWhiteSpace(query.Filter) ? Rows
            : query.Filter.StartsWith("Id gt ", StringComparison.Ordinal) ? Rows.Where(r => r.Id.CompareTo(Guid.Parse(query.Filter[6..])) > 0).ToList()
            : Rows.Where(Matches(query.Filter)).ToList();
        var page = ODataLinq.OrderBy(rows, query.OrderBy).Skip(query.Skip ?? 0).Take(query.Top ?? int.MaxValue).ToList();
        return (ODataPage<T>)(object)new ODataPage<Gadget>(page, rows.Count, query.Facets ? Facets : []);
    }

    /// <summary>The filters of the group counts the server answered.</summary>
    public List<string?> Groupings { get; } = [];

    public Task<IReadOnlyList<ODataGroup>> GroupAsync<T>(string entitySet, string? filter, IReadOnlyList<string> by, CancellationToken cancellationToken = default)
    {
        Groupings.Add(filter);
        var getters = by.Select(PropertyPath.Getter<Gadget>).ToList();
        IReadOnlyList<ODataGroup> groups = [.. Rows.GroupBy(r => string.Join("|", getters.Select(g => g(r))))
            .Select(g => new ODataGroup([.. getters.Select(get => get(g.First()))], g.Count()))];
        return Task.FromResult(groups);
    }

    /// <summary>Holds the row queries back.</summary>
    public Task RowsGate { get; set; } = Task.CompletedTask;

    /// <summary>Row pages of the download after this many fail.</summary>
    public int? FailRowsAfter { get; set; }

    /// <summary>The partition the feed reports: another one than the copy's means another user, tenant or group selection.</summary>
    public string Partition { get; set; } = "p1";

    public string Owner { get; set; } = "o1";

    /// <summary>The changes pages the client asked for, with their skip.</summary>
    public List<string> ChangeRequests { get; } = [];

    /// <summary>The server builds snapshots (SQLite in the browser); without, it answers 404.</summary>
    public bool Snapshots { get; set; } = true;

    /// <summary>Holds the snapshots back.</summary>
    public Task SnapshotGate { get; set; } = Task.CompletedTask;

    public int SnapshotRequests { get; private set; }

    /// <summary>How often the client asked for all visible ids.</summary>
    public int IdsRequests { get; private set; }

    /// <summary>Further pages of changed ids, served to a client that asks with a skip.</summary>
    public Queue<ClientEntityChanges> MoreChanges { get; } = new();

    // the real server knows more of OData than the local copy; answering nothing is enough here
    private static Func<Gadget, bool> Matches(string filter)
    {
        try
        {
            return ODataLinq.Filter<Gadget>(filter).Compile();
        }
        catch (NotSupportedException)
        {
            return _ => false;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Feed;
        var path = request.RequestUri!.AbsolutePath;
        if (Forbidden)
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }

        var query = request.RequestUri.Query;
        if (path.EndsWith("/changes", StringComparison.Ordinal))
        {
            ChangeRequests.Add(query);
        }

        if (path.EndsWith("/ids", StringComparison.Ordinal))
        {
            IdsRequests++;
        }

        if (path.EndsWith("/snapshot", StringComparison.Ordinal))
        {
            SnapshotRequests++;
            await SnapshotGate;
            if (!Snapshots)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // the file of a MemoryStore: the rows as JSON
            var file = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(Rows, JsonSerializerOptions.Web)) };
            file.Headers.Add(ClientEntitySnapshotKinds.VersionHeader, Now.ToString("O"));
            return file;
        }

        object body = path switch
        {
            "/api/v1/client-entities" => new ClientEntityManifest(Partition, [Info], Owner),
            _ when path.EndsWith("/ids", StringComparison.Ordinal) => Rows.Select(r => r.Id).ToArray(),
            _ when query.Contains("skip=", StringComparison.Ordinal) && !query.Contains("skip=0", StringComparison.Ordinal) => MoreChanges.Dequeue() with { Partition = Partition },
            _ when query.Contains("since", StringComparison.Ordinal) => (NextChanges ?? new ClientEntityChanges(Now, Rows.Count, [], [], false)) with { Partition = Partition },
            _ => new ClientEntityChanges(Now, Rows.Count, [], [], false, Partition: Partition),
        };
        if (query.Contains("since", StringComparison.Ordinal) && !query.Contains("skip=", StringComparison.Ordinal) || query.Contains("skip=0", StringComparison.Ordinal))
        {
            NextChanges = null;
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType(), options: JsonSerializerOptions.Web) };
    }
}

internal sealed class MemoryStorage : IClientEntityStorage
{
    public Dictionary<string, byte[]> Saved { get; } = [];

    public Task<byte[]?> LoadAsync(string key) => Task.FromResult(Saved.GetValueOrDefault(key));

    public Task SaveAsync(string key, byte[] data)
    {
        Saved[key] = data;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Saved.Clear();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string prefix)
    {
        foreach (var key in Saved.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            Saved.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <summary>The keys holding data; an empty entry counts as dropped.</summary>
    public IEnumerable<string> Kept => Saved.Where(e => e.Value.Length > 0).Select(e => e.Key).Order(StringComparer.Ordinal);
}

/// <summary>An engine with one store in memory: the sync's store path (as with SQLite) without a database.</summary>
internal sealed class MemoryStore : IClientEntityEngine, IClientEntityStore<Gadget>
{
    public Dictionary<Guid, Gadget> Rows { get; private set; } = [];

    /// <summary>How often the sync read all ids of the store.</summary>
    public int IdReads { get; private set; }

    public bool Disposed { get; private set; }

    public int Count => Rows.Count;

    /// <summary>Asks the server for snapshots, as SQLite does.</summary>
    public bool TakesSnapshots { get; set; }

    public IClientEntityStore<T>? Open<T>(string key) => this as IClientEntityStore<T>;

    public ClientEntitySnapshotRequest? Snapshot(string? expand) => TakesSnapshots ? new ClientEntitySnapshotRequest([], expand) : null;

    public Task ReplaceWithFileAsync(byte[] file, DateTimeOffset version, CancellationToken cancellationToken)
    {
        Rows = JsonSerializer.Deserialize<Gadget[]>(file, JsonSerializerOptions.Web)!.ToDictionary(r => r.Id);
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> RestoreAsync(CancellationToken cancellationToken) => Task.FromResult<DateTimeOffset?>(null);

    public Task ReplaceAsync(IReadOnlyCollection<Gadget> rows, DateTimeOffset version, CancellationToken cancellationToken)
    {
        Rows = rows.ToDictionary(r => r.Id);
        return Task.CompletedTask;
    }

    public Task<bool> ApplyAsync(IReadOnlyCollection<Gadget> changed, IReadOnlyCollection<Guid> deleted, DateTimeOffset version, CancellationToken cancellationToken)
    {
        var touched = deleted.Count(Rows.Remove) > 0;
        foreach (var row in changed)
        {
            touched |= !Rows.TryGetValue(row.Id, out var held) || held != row;
            Rows[row.Id] = row;
        }

        return Task.FromResult(touched);
    }

    public Task<IReadOnlyList<Guid>> IdsAsync(CancellationToken cancellationToken)
    {
        IdReads++;
        return Task.FromResult<IReadOnlyList<Guid>>([.. Rows.Keys]);
    }

    public Task<ODataPage<Gadget>> QueryAsync(ODataQuery query, Func<CancellationToken, Task<IReadOnlyList<FacetGroupDto>>> facets, CancellationToken cancellationToken)
    {
        var rows = string.IsNullOrWhiteSpace(query.Filter) ? Rows.Values.ToList() : Rows.Values.Where(ODataLinq.Filter<Gadget>(query.Filter).Compile()).ToList();
        var page = ODataLinq.OrderBy(rows, query.OrderBy).Skip(query.Skip ?? 0).Take(query.Top ?? int.MaxValue).ToList();
        return Task.FromResult(new ODataPage<Gadget>(page, rows.Count, []));
    }

    public Task DropAsync()
    {
        Rows = [];
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRealtime : Realtime.IRealtimeConnection
{
    private Action<Contracts.Realtime.RealtimeEnvelope>? _handler;

    public event Func<Task>? Reconnected
    {
        add { }
        remove { }
    }

    public List<string> Subscribed { get; } = [];

    public Task StartAsync() => Task.CompletedTask;

    public Task InvokeAsync(string method, string topic)
    {
        Subscribed.Add(topic);
        return Task.CompletedTask;
    }

    public void OnEvent(Action<Contracts.Realtime.RealtimeEnvelope> handler) => _handler = handler;

    public void Raise(string topic) => _handler?.Invoke(new Contracts.Realtime.RealtimeEnvelope(topic, "EntityChanged", JsonSerializer.SerializeToElement(new { })));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
