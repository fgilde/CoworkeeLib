using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Mark read or unread and delete, for one notification.</summary>
public partial class NotificationActions
{
    [Parameter, EditorRequired] public NotificationDto Item { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NotificationCenter Center { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private string ToggleText => Item.ReadAt is null ? L["Mark as read"] : L["Mark as unread"];

    private Task ToggleAsync() =>
        RunAsync(() => Item.ReadAt is null ? Api.MarkNotificationReadAsync(Item.Id) : Api.MarkNotificationUnreadAsync(Item.Id));

    private Task DeleteAsync() => RunAsync(() => Api.DeleteNotificationAsync(Item.Id));

    private async Task RunAsync(Func<Task> action)
    {
        if (await Snackbar.RunAsync(action))
        {
            await Center.RefreshAsync();
        }
    }
}
