using System.Security.Claims;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Users;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Users
{
    private static readonly string[] SearchFields = [nameof(UserRow.Email), nameof(UserRow.FirstName), nameof(UserRow.LastName)];
    private CoworkeeDataTable<UserRow> _table = null!;
    private IReadOnlyList<RoleDto> _roles = [];
    private IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>> _userRoles = new Dictionary<Guid, IReadOnlyList<RoleRefDto>>();
    private bool _canManage;
    private bool _canImpersonate;
    private bool _canViewSessions;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private ClaimsPrincipal _user = new();

    protected override async Task OnInitializedAsync()
    {
        _user = (await AuthenticationState).User;
        _canManage = await MayAsync(IdentityPermissions.Users.Manage);
        _canImpersonate = await MayAsync(IdentityPermissions.Users.Impersonate);
        _canViewSessions = await MayAsync(IdentityPermissions.Sessions.View);
        await Snackbar.RunAsync(async () => _roles = await Api.GetRolesAsync());
    }

    private async Task<bool> MayAsync(string permission) => (await Authorization.AuthorizeAsync(_user, Security.PermissionPolicy.For(permission))).Succeeded;

    private async Task LoadRolesAsync(IReadOnlyList<UserRow> users) =>
        await Snackbar.RunAsync(async () => _userRoles = await Api.GetUsersRolesAsync([.. users.Select(u => u.Id)]));

    // a renamed role shows its new name once the roles reload
    private IEnumerable<string> RolesOf(UserRow user) =>
        _userRoles.GetValueOrDefault(user.Id)?.Select(r => _roles.FirstOrDefault(x => x.Id == r.Id)?.Name ?? r.Name) ?? [];

    // a renamed or new role shows at once
    private Task ReloadRolesAsync() => Snackbar.RunAsync(async () => _roles = await Api.GetRolesAsync());

    private async Task CreateAsync()
    {
        // the groups the administrator reaches (all of the organisation, or his own with group-scoped administration)
        IReadOnlyList<GroupDto> groups = [];
        if (await Permissions.HasAsync(IdentityPermissions.Groups.ManageMembers))
        {
            await Snackbar.RunAsync(async () => groups = (await Api.GetGroupsAsync(new PageRequest(1, 200))).Items);
        }

        if (await Dialogs.ShowEditAsync(L["New user"], new NewUserForm(), SaveAsync, meta => NewUserMeta.Apply(meta, L, _roles, groups)))
        {
            Snackbar.Add(L["User created"], Severity.Success);
            await _table.ReloadAsync();
        }
    }

    private async Task SaveAsync(NewUserForm form)
    {
        var user = await Api.CreateUserAsync(form.ToRequest());
        if (form.SendInvitation && form.IsActive)
        {
            await Api.SendInvitationAsync(user.Id);
        }
    }

    private async Task SetActiveAsync(UserRow user, bool active)
    {
        await Snackbar.RunAsync(() => Api.UpdateUserAsync(user.Id, new UpdateUserRequest(user.FirstName, user.LastName, active)));
        await _table.ReloadAsync();
    }

    // the parts of the user page; the table follows their changes over realtime (type:User)
#pragma warning disable BL0005 // MudEx reads the parameters off these instances and renders components of its own
    private Task ShowRolesAsync(UserRow user) => UserActions.ShowAsync<UserRolesTab>(Dialogs, L["Roles of {0}", Named(user)], t => t.UserId = user.Id);

    private Task ShowGroupsAsync(UserRow user) => UserActions.ShowAsync<UserGroupsTab>(Dialogs, L["Groups of {0}", Named(user)], t => t.UserId = user.Id);

    private Task ShowPermissionsAsync(UserRow user) => UserActions.ShowAsync<UserPermissionsTab>(Dialogs, L["Permissions of {0}", Named(user)], t => t.UserId = user.Id);

    private Task ShowSessionsAsync(UserRow user) => UserActions.ShowAsync<UserSessions>(Dialogs, L["Sessions of {0}", Named(user)], s =>
    {
        s.UserId = user.Id;
        s.UserName = user.Email;
    });
#pragma warning restore BL0005

    private static string Named(UserRow user) => user.Name is { Length: > 0 } name ? name : user.Email;

    private Task ImpersonateAsync(UserRow user) => Impersonation.StartAsync(Dialogs, Api, Snackbar, Nav, L, user.Id, user.Email);

    private Task SendResetAsync(UserRow user) => Snackbar.RunAsync(() => Api.SendPasswordResetAsync(user.Id), L["Password reset mail queued for {0}.", user.Email]);

    private Task SignOutAsync(UserRow user) => UserActions.SignOutAsync(Dialogs, Api, Snackbar, L, user.Id, user.Email);

    private async Task LockAsync(UserRow user)
    {
        if (await UserActions.LockAsync(Dialogs, Api, Snackbar, L, user.Id, user.Email))
        {
            await _table.ReloadAsync();
        }
    }

    private async Task UnlockAsync(UserRow user)
    {
        if (await UserActions.UnlockAsync(Api, Snackbar, L, user.Id))
        {
            await _table.ReloadAsync();
        }
    }

    private async Task DeleteAsync(UserRow user)
    {
        if (await UserActions.DeleteAsync(Dialogs, Api, Snackbar, L, user.Id, user.Email))
        {
            await _table.ReloadAsync();
        }
    }
}
