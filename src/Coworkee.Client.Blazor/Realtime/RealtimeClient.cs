using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coworkee.Client.Blazor.Realtime;

public interface IRealtimeConnection : IAsyncDisposable
{
    event Func<Task>? Reconnected;

    Task StartAsync();

    Task InvokeAsync(string method, string topic);

    void OnEvent(Action<RealtimeEnvelope> handler);
}

internal sealed class SignalRRealtimeConnection : IRealtimeConnection
{
    private readonly HubConnection _connection;

    public SignalRRealtimeConnection(NavigationManager navigation)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(RealtimeHubMethods.Path))
            .WithAutomaticReconnect()
            .Build();
        _connection.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
    }

    public event Func<Task>? Reconnected;

    public Task StartAsync() => _connection.StartAsync();

    public Task InvokeAsync(string method, string topic) => _connection.InvokeAsync(method, topic);

    public void OnEvent(Action<RealtimeEnvelope> handler) => _connection.On(RealtimeHubMethods.OnEvent, handler);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

public sealed class RealtimeClient(IRealtimeConnection connection)
{
    private readonly Dictionary<string, List<Func<RealtimeEnvelope, Task>>> _handlers = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private Task<bool>? _started;

    public async Task<IAsyncDisposable> SubscribeAsync(string topic, Func<RealtimeEnvelope, Task> handler)
    {
        if (!await (_started ??= StartAsync()))
        {
            return NoSubscription.Instance;
        }

        bool first;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(topic, out var handlers))
            {
                _handlers[topic] = handlers = [];
            }

            first = handlers.Count == 0;
            handlers.Add(handler);
        }

        if (first && !await TryInvokeAsync(RealtimeHubMethods.Subscribe, topic))
        {
            Remove(topic, handler);
            return NoSubscription.Instance;
        }

        return new Subscription(this, topic, handler);
    }

    private async Task<bool> StartAsync()
    {
        connection.OnEvent(Dispatch);
        connection.Reconnected += ResubscribeAsync;
        try
        {
            await connection.StartAsync();
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private void Dispatch(RealtimeEnvelope envelope)
    {
        Func<RealtimeEnvelope, Task>[] handlers;
        lock (_lock)
        {
            handlers = _handlers.TryGetValue(envelope.Topic, out var list) ? [.. list] : [];
        }

        foreach (var handler in handlers)
        {
            _ = handler(envelope);
        }
    }

    private async Task ResubscribeAsync()
    {
        string[] topics;
        lock (_lock)
        {
            topics = [.. _handlers.Where(h => h.Value.Count > 0).Select(h => h.Key)];
        }

        foreach (var topic in topics)
        {
            await TryInvokeAsync(RealtimeHubMethods.Subscribe, topic);
        }
    }

    private bool Remove(string topic, Func<RealtimeEnvelope, Task> handler)
    {
        lock (_lock)
        {
            return _handlers.TryGetValue(topic, out var handlers) && handlers.Remove(handler) && handlers.Count == 0;
        }
    }

    private async Task<bool> TryInvokeAsync(string method, string topic)
    {
        try
        {
            await connection.InvokeAsync(method, topic);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private sealed class Subscription(RealtimeClient client, string topic, Func<RealtimeEnvelope, Task> handler) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && client.Remove(topic, handler))
            {
                await client.TryInvokeAsync(RealtimeHubMethods.Unsubscribe, topic);
            }
        }
    }

    private sealed class NoSubscription : IAsyncDisposable
    {
        public static readonly NoSubscription Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
