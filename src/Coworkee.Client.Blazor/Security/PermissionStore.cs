using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Security;

/// <summary>
/// The signed-in user's permissions, loaded once and reloaded when the server reports a change of roles, grants, memberships or
/// the active role; <see cref="Changed"/> lets navigation and page guards evaluate again.
/// </summary>
public sealed class PermissionStore(ICoworkeeApi api, RealtimeClient realtime) : IAsyncDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private Task<HashSet<string>>? _permissions;
    private Task? _subscribed;
    private CancellationTokenSource? _pending;

    public event Action? Changed;

    public async Task<bool> HasAsync(string permission) => (await (_permissions ??= LoadAsync())).Contains(permission);

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
            return new HashSet<string>(await api.GetMyPermissionsAsync(), StringComparer.Ordinal);
        }
        catch (ApiException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
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
    }
}
