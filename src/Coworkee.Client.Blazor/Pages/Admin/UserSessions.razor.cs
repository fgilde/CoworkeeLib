using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The sessions of one user on his admin page, live: end one or all, set his own limit.</summary>
public partial class UserSessions : IAsyncDisposable
{
    private UserSessionsDto? _sessions;
    private bool _canManage;
    private readonly List<IAsyncDisposable> _subscriptions = [];

    [Parameter, EditorRequired] public Guid UserId { get; set; }

    [Parameter] public string UserName { get; set; } = string.Empty;

    [Inject] private ISessionsApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        _canManage = (await Authorization.AuthorizeAsync((await AuthenticationState).User, PermissionPolicy.For(IdentityPermissions.Sessions.Manage))).Succeeded;
        // any session of the organisation and the user's own limit (a column of the user)
        foreach (var topic in new[] { RealtimeTopics.Type("UserSession"), RealtimeTopics.Entity("User", UserId.ToString()) })
        {
            _subscriptions.Add(await Realtime.SubscribeAsync(topic, _ => InvokeAsync(async () =>
            {
                await LoadAsync();
                StateHasChanged();
            })));
        }

        await LoadAsync();
    }

    private Task<bool> LoadAsync() => Snackbar.RunAsync(async () => _sessions = await Api.GetOfUserAsync(UserId));

    private async Task EndAsync(UserSessionDto session)
    {
        if (await Dialogs.ConfirmAsync(L["End session"], L["Sign {0} out of {1}?", UserName, ProfileSessions.Describe(session)], L["End"], L["Cancel"], Icons.Material.Outlined.Logout)
            && await Snackbar.RunAsync(() => Api.EndAsync(session.Id), L["Session ended"]))
        {
            await LoadAsync();
        }
    }

    private async Task EndAllAsync()
    {
        if (await Dialogs.ConfirmAsync(L["End all sessions"], L["Sign {0} out everywhere?", UserName], L["End"], L["Cancel"], Icons.Material.Outlined.Logout)
            && await Snackbar.RunAsync(() => Api.EndAllAsync(UserId), L["Signed out"]))
        {
            await LoadAsync();
        }
    }

    private async Task EditLimitAsync()
    {
        if (await SessionLimits.EditAsync(Dialogs, Api, L, UserId, UserName, _sessions?.MaxSessions))
        {
            await LoadAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }
    }
}
