using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The entities the user follows, newest first, with a link to each and Unfollow.</summary>
public partial class FollowedEntities
{
    private List<FollowDto>? _follows;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        if (!await Snackbar.RunAsync(async () => _follows = [.. await Api.GetFollowsAsync()]))
        {
            _follows = [];
        }
    }

    private async Task UnfollowAsync(FollowDto follow)
    {
        if (await Snackbar.RunAsync(() => Api.UnfollowAsync(follow.EntityType, follow.EntityId)))
        {
            _follows!.Remove(follow);
        }
    }
}
