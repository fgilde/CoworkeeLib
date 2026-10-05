using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Components;

public partial class NotificationBell : IAsyncDisposable
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    private int _count;
    private IReadOnlyList<NotificationDto> _items = [];
    private IAsyncDisposable? _subscription;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await RefreshCountAsync();
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out var userId))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(userId), _ => InvokeAsync(async () =>
            {
                await RefreshCountAsync();
                StateHasChanged();
            }));
        }
    }

    private async Task RefreshCountAsync()
    {
        try
        {
            _count = (await Api.GetUnreadNotificationCountAsync())?.Count ?? 0;
        }
        catch (ApiException)
        {
        }
    }

    private async Task OpenChangedAsync(bool open)
    {
        if (open)
        {
            try
            {
                _items = (await Api.GetNotificationsAsync(false, new Coworkee.Contracts.PageRequest(1, 10))).Items;
            }
            catch (ApiException)
            {
            }
        }
    }

    private async Task OpenAsync(NotificationDto item)
    {
        if (item.ReadAt is null)
        {
            await Api.MarkNotificationReadAsync(item.Id);
            await RefreshCountAsync();
        }

        if (item.Link is { Length: > 0 } link && link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal))
        {
            Nav.NavigateTo(link);
        }
    }

    private async Task MarkAllAsync()
    {
        await Api.MarkAllNotificationsReadAsync();
        await RefreshCountAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }
}
