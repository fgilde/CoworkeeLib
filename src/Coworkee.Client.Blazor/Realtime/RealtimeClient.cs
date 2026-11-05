using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Realtime;

public sealed class RealtimeClient(IRealtimeConnection connection)
{
    private readonly Dictionary<string, List<Func<RealtimeEnvelope, Task>>> _handlers = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private Task<bool>? _started;

    /// <summary>
    /// Follows a topic. The handler stays even while the hub is not reachable: a failed start is retried in the background and then subscribes every
    /// followed topic, so a component that subscribes once (the layout's settings) still gets its events.
    /// </summary>
    public async Task<IAsyncDisposable> SubscribeAsync(string topic, Func<RealtimeEnvelope, Task> handler)
    {
        bool first;
        Task<bool> started;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(topic, out var handlers))
            {
                _handlers[topic] = handlers = [];
            }

            first = handlers.Count == 0;
            handlers.Add(handler);
            started = _started ??= StartAsync();
        }

        // a subscription that fails now is made again by the retry or the next reconnect
        if (await started && first)
        {
            await TryInvokeAsync(RealtimeHubMethods.Subscribe, topic);
        }

        return new Subscription(this, topic, handler);
    }

    /// <summary>True for every subscription <see cref="SubscribeAsync"/> returns: one made while the hub was not reachable follows once it is.</summary>
    public static bool IsActive(IAsyncDisposable subscription) => subscription is Subscription;

    private bool _wired;

    private async Task<bool> StartAsync()
    {
        if (!_wired)
        {
            _wired = true;
            connection.OnEvent(Dispatch);
            connection.Reconnected += ResubscribeAsync;
        }

        if (await TryStartAsync())
        {
            return true;
        }

        _ = RetryStartAsync();
        return false;
    }

    // ponytail: retries for the life of the client (the app's lifetime in the browser), at most every 30 seconds
    private async Task RetryStartAsync()
    {
        // never on the caller's stack: StartAsync runs under the lock and its result is stored after it returns
        await Task.Yield();
        var policy = new ForeverRetryPolicy();
        for (var attempt = 0; ; attempt++)
        {
            await Task.Delay(policy.NextRetryDelay(new Microsoft.AspNetCore.SignalR.Client.RetryContext { PreviousRetryCount = attempt }) ?? TimeSpan.FromSeconds(30));
            if (await TryStartAsync())
            {
                lock (_lock)
                {
                    _started = Task.FromResult(true);
                }

                await ResubscribeAsync();
                return;
            }
        }
    }

    private async Task<bool> TryStartAsync()
    {
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

    /// <summary>Subscribes every followed topic again, e.g. after the permissions grew: the server takes back topics, never returns them.</summary>
    public async Task ResubscribeAsync()
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
}
