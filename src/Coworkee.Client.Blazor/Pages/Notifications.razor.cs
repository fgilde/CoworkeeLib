using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages;

public partial class Notifications
{
    /// <summary>Shown as a tab of the account page: without own title.</summary>
    [Parameter] public bool Embedded { get; set; }

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    private const int PageSize = 25;

    private PagedResult<NotificationDto>? _page;
    private bool _unreadOnly;
    private int _number = 1;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _page = await Api.GetNotificationsAsync(_unreadOnly, new PageRequest(_number, PageSize));
        }
        catch (ApiException)
        {
            _page = new PagedResult<NotificationDto>([], 0, 1, PageSize);
        }
    }

    private Task FilterAsync(bool unreadOnly)
    {
        (_unreadOnly, _number) = (unreadOnly, 1);
        return LoadAsync();
    }

    private Task PageAsync(int number)
    {
        _number = number;
        return LoadAsync();
    }

    private async Task MarkAsync(NotificationDto item)
    {
        await Api.MarkNotificationReadAsync(item.Id);
        await LoadAsync();
    }

    private async Task MarkAllAsync()
    {
        await Api.MarkAllNotificationsReadAsync();
        await LoadAsync();
    }

    private async Task OpenAsync(NotificationDto item)
    {
        if (item.ReadAt is null)
        {
            await Api.MarkNotificationReadAsync(item.Id);
        }

        if (item.Link is { Length: > 0 } link && link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal))
        {
            Nav.NavigateTo(link);
        }
        else
        {
            await LoadAsync();
        }
    }
}
