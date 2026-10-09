using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Realtime;

/// <summary>
/// The signed-in user's unread notifications, kept live over the user's realtime topic: every change (here, in another tab or by
/// the server) refreshes the count and raises <see cref="Changed"/> once, also for bursts like "mark all as read".
/// </summary>
public sealed class NotificationCenter(
    ICoworkeeApi api, RealtimeClient realtime, AuthenticationStateProvider authentication, NavigationManager navigation) : IAsyncDisposable
{
    private const string EntityType = "Notification";
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);
    private Task? _started;
    private IAsyncDisposable? _subscription;
    private CancellationTokenSource? _pending;
    private int _arrived;

    public int UnreadCount { get; private set; }

    /// <summary>Notifications changed; the argument counts the ones that arrived.</summary>
    public event Action<int>? Changed;

    public Task StartAsync() => _started ??= StartCoreAsync();

    /// <summary>Reloads the count and tells every listener, e.g. after an own change.</summary>
    public async Task RefreshAsync(int arrived = 0)
    {
        try
        {
            UnreadCount = (await api.GetUnreadNotificationCountAsync())?.Count ?? 0;
        }
        catch (ApiException)
        {
        }

        Changed?.Invoke(arrived);
    }

    /// <summary>Marks the notification read and goes to its link when that is a page of this app.</summary>
    public async Task OpenAsync(NotificationDto item)
    {
        if (item.ReadAt is null)
        {
            try
            {
                await api.MarkNotificationReadAsync(item.Id);
            }
            catch (ApiException)
            {
            }

            await RefreshAsync();
        }

        if (item.Link is { Length: > 0 } link && link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal))
        {
            navigation.NavigateTo(link);
        }
    }

    private async Task StartCoreAsync()
    {
        await RefreshAsync();
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (Guid.TryParse(user.FindFirst("sub")?.Value, out var userId))
        {
            _subscription = await realtime.SubscribeAsync(RealtimeTopics.User(userId), OnEventAsync);
        }
    }

    private async Task OnEventAsync(RealtimeEnvelope envelope)
    {
        if (envelope.Type != RealtimeEventTypes.EntityChanged
            || envelope.Payload.Deserialize<EntityChangedPayload>(JsonSerializerOptions.Web) is not { EntityType: EntityType } change)
        {
            return;
        }

        if (change.Action == "Created")
        {
            _arrived++;
        }

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

        var arrived = _arrived;
        _arrived = 0;
        await RefreshAsync(arrived);
    }

    public async ValueTask DisposeAsync()
    {
        _pending?.Cancel();
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }
}
