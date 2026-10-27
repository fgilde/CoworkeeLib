using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>The inbox (open, done or all, by category and text), the user's notification preferences and the entities he follows.</summary>
public partial class Notifications : IDisposable
{
    /// <summary>Shown as a tab of the account page: without own title.</summary>
    [Parameter] public bool Embedded { get; set; }

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NotificationCenter Center { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private const int PageSize = 25;

    private PagedResult<NotificationDto>? _page;
    private NotificationCountsDto? _counts;
    private bool _unreadOnly;
    private string _state = "open";
    private string? _category;
    private string? _search;
    private int _number = 1;

    protected override async Task OnInitializedAsync()
    {
        Center.Changed += OnChanged;
        await LoadAsync();
        await Center.StartAsync();
    }

    private void OnChanged(int arrived) => _ = InvokeAsync(async () =>
    {
        await LoadAsync();
        StateHasChanged();
    });

    private NotificationFilter Filter => new(_unreadOnly, _category, _state switch { "open" => false, "done" => true, _ => null }, _search);

    private async Task LoadAsync()
    {
        try
        {
            _page = await Api.GetNotificationsAsync(Filter, new PageRequest(_number, PageSize));
            _counts = await Api.GetNotificationCountsAsync();
            if (_page?.Items.Count == 0 && _number > 1)
            {
                _number--;
                await LoadAsync();
            }
        }
        catch (ApiException)
        {
            _page ??= new PagedResult<NotificationDto>([], 0, 1, PageSize);
        }
    }

    private Task FilterAsync(bool unreadOnly)
    {
        (_unreadOnly, _number) = (unreadOnly, 1);
        return LoadAsync();
    }

    private Task StateAsync(string? state)
    {
        (_state, _number) = (state ?? "open", 1);
        return LoadAsync();
    }

    private Task CategoryAsync(string? category)
    {
        (_category, _number) = (category, 1);
        return LoadAsync();
    }

    private Task SearchAsync(string? search)
    {
        (_search, _number) = (search, 1);
        return LoadAsync();
    }

    private Task PageAsync(int number)
    {
        _number = number;
        return LoadAsync();
    }

    private async Task MarkAllAsync()
    {
        if (await Snackbar.RunAsync(() => Api.MarkAllNotificationsReadAsync()))
        {
            await Center.RefreshAsync();
        }
    }

    private async Task DeleteAllAsync()
    {
        if (await Dialogs.ConfirmAsync(L["Delete all"], L["Delete all notifications? This cannot be undone."], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteSweep)
            && await Snackbar.RunAsync(() => Api.DeleteAllNotificationsAsync()))
        {
            await Center.RefreshAsync();
        }
    }

    public void Dispose() => Center.Changed -= OnChanged;
}
