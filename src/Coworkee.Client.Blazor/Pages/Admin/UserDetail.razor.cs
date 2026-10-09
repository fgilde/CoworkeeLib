using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.People;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class UserDetail
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public Guid Id { get; set; }

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    private UserDetailDto? _user;
    private IReadOnlyList<RoleDto> _roles = [];
    private IReadOnlyList<GroupDto> _groups = [];
    private IReadOnlyList<string> _permissions = [];
    private IReadOnlyCollection<Guid> _roleIds = [];
    private IReadOnlyCollection<Guid> _groupIds = [];
    private UserForm _form = new();
    private ObjectEditMeta<UserForm>? _meta;
    private string? _language;
    private bool _canManage;
    private bool _canManageGroups;
    private bool _busy;
    private string? _error;

    protected override async Task OnParametersSetAsync()
    {
        var user = (await AuthenticationState).User;
        _canManage = (await Authorization.AuthorizeAsync(user, Security.PermissionPolicy.For(IdentityPermissions.Users.Manage))).Succeeded;
        _canManageGroups = (await Authorization.AuthorizeAsync(user, Security.PermissionPolicy.For(IdentityPermissions.Groups.Manage))).Succeeded;
        try
        {
            _roles = await Api.GetRolesAsync();
            _groups = _canManageGroups ? (await Api.GetGroupsAsync(new PageRequest(1, 200))).Items : [];
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 404 ? L["This user does not exist."] : exception.Message;
        }
    }

    private async Task LoadAsync()
    {
        _user = await Api.GetUserDetailAsync(Id);
        _permissions = await Api.GetEffectivePermissionsAsync(Id);
        _language = (await Api.GetUserLanguageAsync(Id))?.Culture;
        _form = UserForm.From(_user, _language);
        _meta = _form.ObjectEditMeta(meta => UserFormMeta.Apply(meta, L, !_canManage));
        _roleIds = _user.Roles.Select(r => r.Id).ToList();
        _groupIds = _user.Groups.Select(g => g.Id).ToList();
    }

    private static string DisplayName(UserDetailDto user) =>
        $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : user.Email;

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private string GroupName(Guid id) => _groups.FirstOrDefault(g => g.Id == id)?.Name ?? string.Empty;

    private Task SaveAsync(EditContext context) => RunAsync(async () =>
    {
        await Api.UpdateUserAsync(Id, _form.ToRequest());
        var language = _form.Language.Length == 0 ? null : _form.Language;
        if (language != _language)
        {
            await Api.SetUserLanguageAsync(Id, language);
        }

        Cards.Forget(Id);
    }, L["Saved"]);

    private Task UnlockAsync() => RunAsync(() => Api.UnlockUserAsync(Id), L["Unlocked"]);

    private async Task UploadAvatarAsync(IBrowserFile? file)
    {
        if (file is null)
        {
            return;
        }

        if (await AvatarPicture.ReadAsync(JS, file) is not { } picture)
        {
            Snackbar.Add(L["This file is no picture the browser can show."], Severity.Warning);
            return;
        }

        await RunAsync(async () =>
        {
            await Api.SetUserAvatarAsync(Id, picture);
            Cards.Forget(Id);
        }, L["Picture saved"]);
    }

    private Task RemoveAvatarAsync() => RunAsync(async () =>
    {
        await Api.SetUserAvatarAsync(Id, null);
        Cards.Forget(Id);
    }, L["Picture removed"]);

    private async Task LockAsync()
    {
        if (await Dialogs.ShowEditAsync(L["Lock {0}", _user!.Email], new LockForm()) is { } form)
        {
            var until = form.Until is { } date ? new DateTimeOffset(date.Date.AddDays(1), DateTimeOffset.Now.Offset) : (DateTimeOffset?)null;
            await RunAsync(() => Api.LockUserAsync(Id, until), L["Locked; the user was signed out everywhere."]);
        }
    }

    private async Task SignOutAsync()
    {
        if (await Dialogs.ConfirmAsync(L["Sign out everywhere"], L["End all sessions of {0}? Open windows sign out right away.", _user!.Email], L["Sign out"], L["Cancel"],
                Icons.Material.Outlined.Logout))
        {
            await RunAsync(() => Api.SignOutUserAsync(Id), L["The user was signed out everywhere."]);
        }
    }

    private sealed class LockForm
    {
        /// <summary>Locked through this day; empty locks until an administrator unlocks.</summary>
        [Display(Name = "Locked through (empty: until unlocked)")]
        public DateTime? Until { get; set; }
    }

    private async Task DeleteAsync()
    {
        if (!await Dialogs.ConfirmAsync(L["Delete user"], L["Delete {0} and all personal data? This cannot be undone.", _user!.Email], L["Delete"], L["Cancel"],
                Icons.Material.Outlined.DeleteForever))
        {
            return;
        }

        if (await Snackbar.RunAsync(() => Api.DeleteUserAsync(Id), L["User deleted"]))
        {
            Nav.NavigateTo("/admin/users");
        }
    }

    private Task SetRolesAsync(IReadOnlyCollection<Guid> ids) => RunAsync(() => Api.SetUserRolesAsync(Id, ids.ToList()), L["Roles saved"]);

    private Task SetGroupsAsync(IReadOnlyCollection<Guid> ids) => RunAsync(() => Api.SetUserGroupsAsync(Id, ids.ToList()), L["Groups saved"]);

    private async Task RunAsync(Func<Task> action, string done)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await action();
            Snackbar.Add(done, Severity.Success);
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }
}
