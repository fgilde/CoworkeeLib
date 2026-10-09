using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The unread count in the app bar, the latest notifications on click and a snackbar for each that arrives.</summary>
public partial class NotificationBell : IDisposable
{
    private const int Latest = 10;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NotificationCenter Center { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private IReadOnlyList<NotificationDto> _items = [];
    private bool _open;

    protected override async Task OnInitializedAsync()
    {
        Center.Changed += OnChanged;
        await Center.StartAsync();
    }

    private void OnChanged(int arrived) => _ = InvokeAsync(async () =>
    {
        if (_open)
        {
            await LoadAsync();
        }

        if (arrived > 0)
        {
            await AnnounceAsync(arrived);
        }

        StateHasChanged();
    });

    private async Task LoadAsync()
    {
        try
        {
            _items = (await Api.GetNotificationsAsync(false, new PageRequest(1, Latest))).Items;
        }
        catch (ApiException)
        {
        }
    }

    private async Task AnnounceAsync(int arrived)
    {
        NotificationDto? latest = null;
        if (arrived == 1)
        {
            try
            {
                latest = (await Api.GetNotificationsAsync(true, new PageRequest(1, 1))).Items.FirstOrDefault();
            }
            catch (ApiException)
            {
            }
        }

        var text = latest is null ? L["{0} new notifications", arrived] : L.Server(latest.Title, latest.Arguments);
        Snackbar.Add(text, Severity.Info, options =>
        {
            options.Icon = Icons.Material.Outlined.Notifications;
            options.Action = L["Show"];
            options.OnClick = _ => latest is null ? NavigateToAll() : Center.OpenAsync(latest);
        });
    }

    private Task NavigateToAll()
    {
        Nav.NavigateTo("/notifications");
        return Task.CompletedTask;
    }

    private async Task OpenChangedAsync(bool open)
    {
        _open = open;
        if (open)
        {
            await LoadAsync();
        }
    }

    private Task OpenAsync(NotificationDto item) => Center.OpenAsync(item);

    private async Task MarkAllAsync()
    {
        if (await Snackbar.RunAsync(() => Api.MarkAllNotificationsReadAsync()))
        {
            await Center.RefreshAsync();
        }
    }

    public void Dispose() => Center.Changed -= OnChanged;
}
