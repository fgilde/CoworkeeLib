using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Follow or unfollow one entity of a type registered with <c>AddNotificationTypes(n => n.Followable&lt;T&gt;(...))</c>; hidden when the user may not follow it.</summary>
public partial class CoworkeeFollowButton
{
    private FollowStateDto? _state;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    /// <summary>How the entity shows in the list of followed ones, e.g. its number.</summary>
    [Parameter] public string? Title { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            _state = await Api.GetFollowStateAsync(EntityType, EntityId);
        }
        catch (ApiException)
        {
            _state = null;
        }
    }

    private async Task ToggleAsync()
    {
        _busy = true;
        var following = _state!.Following;
        await Snackbar.RunAsync(async () => _state = following ? await Api.UnfollowAsync(EntityType, EntityId) : await Api.FollowAsync(EntityType, EntityId, Title));
        _busy = false;
    }
}
