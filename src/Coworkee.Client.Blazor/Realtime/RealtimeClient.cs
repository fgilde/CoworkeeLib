using System.Text.Json;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Realtime;

// the services, not the authentication state provider: the BFF's provider reaches this client through the permission store (a cycle in the constructors)
public sealed class RealtimeClient(IRealtimeConnection connection, IServiceProvider? services = null)
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

    /// <summary>
    /// Typed events, with the subscriptions, reconnects and resubscribes of <see cref="SubscribeAsync"/>:
    /// <list type="bullet">
    /// <item><c>On&lt;EntityChanged&lt;OrderDto&gt;&gt;(c => ...)</c>: changes of the entity on <c>type:Order</c> (the DTO's name without "Dto"), with who and when;
    /// <paramref name="topic"/> follows another one, e.g. <c>entity:Order:{id}</c>.</item>
    /// <item><c>On&lt;OrderApproved&gt;(e => ...)</c>: an event of the server's <c>RealtimeEvents</c> under its stable name (<see cref="EventNameAttribute"/>), on every topic
    /// an event target reaches (the user's, the organisation's, the global one, the session's), or on <paramref name="topic"/>.</item>
    /// </list>
    /// <paramref name="skipOwn"/> leaves out what the signed-in user caused himself (the envelope's actor).
    /// </summary>
    public async Task<IAsyncDisposable> On<T>(Func<T, Task> handler, string? topic = null, bool skipOwn = false)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var user = services?.GetService(typeof(AuthenticationStateProvider)) is AuthenticationStateProvider authentication
            ? (await authentication.GetAuthenticationStateAsync()).User
            : null;
        Guid? me = ClientUser.From(user).TryGetUserId(out var id) ? id : null;
        var entity = typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(EntityChanged<>);
        var type = entity ? RealtimeEventTypes.EntityChanged : EventNames.Of<T>();
        List<string> topics = topic is { Length: > 0 } ? [topic]
            : entity ? [RealtimeTopics.Type((string)typeof(T).GetProperty(nameof(EntityChanged<object>.Entity))!.GetValue(null)!)]
            : [.. Targeted(user, me)];

        Task Receive(RealtimeEnvelope envelope)
        {
            if (envelope.Type != type || (skipOwn && me is not null && envelope.Actor?.UserId == me))
            {
                return Task.CompletedTask;
            }

            var value = entity ? Entity<T>(envelope) : envelope.Payload.Deserialize<T>(JsonSerializerOptions.Web);
            return value is null ? Task.CompletedTask : handler(value);
        }

        var subscriptions = new List<IAsyncDisposable>();
        foreach (var t in topics)
        {
            subscriptions.Add(await SubscribeAsync(t, Receive));
        }

        return new Subscriptions(subscriptions);
    }

    // the topics an event target can reach a signed-in user on
    private static IEnumerable<string> Targeted(System.Security.Claims.ClaimsPrincipal? user, Guid? me)
    {
        if (me is { } id)
        {
            yield return RealtimeTopics.User(id);
        }

        yield return RealtimeTopics.TenantEvents;
        yield return RealtimeTopics.GlobalEvents;
        if (user?.FindFirst("sid")?.Value is { Length: > 0 } sid)
        {
            yield return RealtimeTopics.Session(sid);
        }
    }

    private static T? Entity<T>(RealtimeEnvelope envelope) =>
        envelope.Payload.Deserialize<EntityChangedPayload>(JsonSerializerOptions.Web) is { } change
            ? (T)Activator.CreateInstance(typeof(T), change.EntityType, change.EntityId, change.Action, change.ChangedProperties, envelope.Actor, envelope.At)!
            : default;

    private sealed class Subscriptions(List<IAsyncDisposable> subscriptions) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var subscription in subscriptions)
            {
                await subscription.DisposeAsync();
            }
        }
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
