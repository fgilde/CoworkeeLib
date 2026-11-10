using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Security;

/// <summary>
/// The signed-in user's permissions, loaded once and reloaded when the server reports a change of roles, grants, memberships or
/// the active role; <see cref="Changed"/> lets navigation and page guards evaluate again. Only a refusal (401, 403) means no permissions: when the
/// server does not answer (5xx, no connection) the last known permissions stay, <see cref="Unavailable"/> says so and the load is tried again, so a
/// short outage neither empties the navigation nor sends the user to "you lack the permission".
/// </summary>
public sealed class PermissionStore(ICoworkeeApi api, RealtimeClient realtime) : IAsyncDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the store waits before asking again after the server did not answer.</summary>
    internal static TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(5);

    private HashSet<string>? _known;
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private Task<HashSet<string>>? _permissions;
    private Task? _subscribed;
    private CancellationTokenSource? _pending;

    public event Action? Changed;

    /// <summary>The last load found the server unreachable; the permissions shown are the last known ones (or none before the first answer).</summary>
    public bool Unavailable { get; private set; }

    public async Task<bool> HasAsync(string permission) => (await (_permissions ??= LoadAsync())).Contains(permission);

    /// <summary>Any or all of <paramref name="permissions"/>; an empty list restricts nothing.</summary>
    public async Task<bool> HasAsync(IReadOnlyCollection<string> permissions, PermissionMatch match)
    {
        if (permissions.Count == 0)
        {
            return true;
        }

        var granted = await (_permissions ??= LoadAsync());
        return match == PermissionMatch.All ? permissions.All(granted.Contains) : permissions.Any(granted.Contains);
    }

    public void Reset()
    {
        _permissions = null;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _pending?.Cancel();
        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }
    }

    private async Task<HashSet<string>> LoadAsync()
    {
        await (_subscribed ??= SubscribeAsync());
        try
        {
            var loaded = new HashSet<string>(await api.GetMyPermissionsAsync(), StringComparer.Ordinal);
            _known = loaded;
            Unavailable = false;
            return loaded;
        }
        catch (ApiException exception) when (exception.Status is 401 or 403)
        {
            _known = null;
            Unavailable = false;
            return new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            Unavailable = true;
            _ = RetryAsync();
            return _known ?? new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private async Task RetryAsync()
    {
        _pending?.Cancel();
        var pending = _pending = new CancellationTokenSource();
        try
        {
            await Task.Delay(RetryAfter, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Reset();
    }

    private async Task SubscribeAsync()
    {
        foreach (var topic in new[] { RealtimeTopics.TenantPermissions, RealtimeTopics.GlobalPermissions })
        {
            _subscriptions.Add(await realtime.SubscribeAsync(topic, _ => SettleAsync()));
        }
    }

    // a role save touches several rows; one reload for the burst
    private async Task SettleAsync()
    {
        _pending?.Cancel();
        var pending = _pending = new CancellationTokenSource();
        try
        {
            await Task.Delay(Settle, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Reset();
        await realtime.ResubscribeAsync();
    }
}
