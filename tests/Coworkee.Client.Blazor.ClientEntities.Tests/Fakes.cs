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

    public int ParallelPages { get; set; } = 2;

    public string? Expand { get; set; }

    public ClientEntitySetInfo Info => new("Gadgets", BatchSize, MaxRows, TimeSpan.Zero, RealtimeTopic, Expand, ParallelPages);

    public ClientEntitySet<Gadget> Set(IClientEntityStorage? storage = null, string partition = "p1") =>
        new(Info, "Brand", partition, this, Api, storage ?? new NoClientEntityStorage(), NullLogger.Instance);

    public ClientEntityApi Api => new(new HttpClient(this) { BaseAddress = new Uri("http://app/") });

    public ClientEntities Entities(IClientEntityStorage? storage = null) =>
        new(Api, this, storage ?? new NoClientEntityStorage(), null, new ClientEntitySignal(), NullLogger<ClientEntities>.Instance);

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

    /// <summary>Holds the row queries back.</summary>
    public Task RowsGate { get; set; } = Task.CompletedTask;

    /// <summary>Row pages of the download after this many fail.</summary>
    public int? FailRowsAfter { get; set; }

    /// <summary>The partition the feed reports: another one than the copy's means another user, tenant or group selection.</summary>
    public string Partition { get; set; } = "p1";

    public string Owner { get; set; } = "o1";

    /// <summary>The changes pages the client asked for, with their skip.</summary>
    public List<string> ChangeRequests { get; } = [];

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
