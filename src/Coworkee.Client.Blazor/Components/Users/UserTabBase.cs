using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// A part of the admin user page that stands alone (<see cref="UserId"/> is all it needs), so the users list shows the same component in a dialog.
/// Loads when the user changes and again when someone changes roles, groups or grants of the organisation.
/// </summary>
public abstract class UserTabBase : CoworkeeComponentBase
{
    private Guid _loaded;

    [Parameter, EditorRequired] public Guid UserId { get; set; }

    [Inject] protected ICoworkeeApi Api { get; set; } = null!;

    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    [Inject] protected CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    protected bool Loaded { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        if (UserId == _loaded)
        {
            return;
        }

        _loaded = UserId;
        await PrepareAsync();
        Loaded = await Snackbar.RunAsync(LoadAsync);
    }

    protected async Task<bool> MayAsync(string permission) =>
        (await Authorization.AuthorizeAsync((await AuthenticationState).User, PermissionPolicy.For(permission))).Succeeded;

    /// <summary>Once per user before the first load, e.g. the permission checks.</summary>
    protected virtual Task PrepareAsync() => Task.CompletedTask;

    /// <summary>Reads the saved state of the user; toggles that are not saved yet stay shown on top of it.</summary>
    protected abstract Task LoadAsync();

    // the topic of every role, group, membership and grant change of the organisation (IdentityRealtimeTopics)
    // ponytail: one load per event, also for the own saves; debounce when bursts of many changes make it noticeable
    protected async Task ReloadAsync() => Loaded = await Snackbar.RunAsync(LoadAsync) || Loaded;

    /// <summary>The snackbar of a save that failed; the item already shows its saved state again.</summary>
    protected async Task SettledAsync(string? failure, string item, Action saved)
    {
        if (failure is null)
        {
            saved();
        }
        else
        {
            Snackbar.Add(L["{0} was not saved: {1}", item, failure], Severity.Error);
        }

        await InvokeAsync(StateHasChanged);
    }
}
