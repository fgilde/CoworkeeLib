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

    public ClientEntitySetInfo Info => new("Gadgets", BatchSize, MaxRows, TimeSpan.Zero, RealtimeTopic);

    public ClientEntitySet<Gadget> Set(IClientEntityStorage? storage = null) =>
        new(Info, "Brand", "p1", this, new ClientEntityApi(new HttpClient(this) { BaseAddress = new Uri("http://app/") }), storage ?? new NoClientEntityStorage(), NullLogger.Instance);

    public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        var rows = string.IsNullOrWhiteSpace(query.Filter) ? Rows : Rows.Where(Matches(query.Filter)).ToList();
        var page = ODataLinq.OrderBy(rows, query.OrderBy).Skip(query.Skip ?? 0).Take(query.Top ?? int.MaxValue).ToList();
        return Task.FromResult((ODataPage<T>)(object)new ODataPage<Gadget>(page, rows.Count, query.Facets ? Facets : []));
    }

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

        object body = path switch
        {
            "/api/v1/client-entities" => new ClientEntityManifest("p1", [Info]),
            _ when path.EndsWith("/ids", StringComparison.Ordinal) => Rows.Select(r => r.Id).ToArray(),
            _ when request.RequestUri.Query.Contains("since", StringComparison.Ordinal) => NextChanges ?? new ClientEntityChanges(Now, Rows.Count, [], [], false),
            _ => new ClientEntityChanges(Now, Rows.Count, [], [], false),
        };
        NextChanges = null;
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
