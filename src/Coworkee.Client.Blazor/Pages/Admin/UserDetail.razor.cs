using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class UserDetail
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public Guid Id { get; set; }

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    private UserDetailDto? _user;
    private IReadOnlyList<RoleDto> _roles = [];
    private IReadOnlyList<string> _permissions = [];
    private IReadOnlyCollection<Guid> _roleIds = [];
    private string? _firstName;
    private string? _lastName;
    private bool _active;
    private bool _mustChangePassword;
    private bool _canManage;
    private bool _busy;
    private string? _error;

    protected override async Task OnParametersSetAsync()
    {
        _canManage = (await Authorization.AuthorizeAsync((await AuthenticationState).User, Security.PermissionPolicy.For(IdentityPermissions.Users.Manage))).Succeeded;
        try
        {
            _roles = await Api.GetRolesAsync();
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 404 ? "This user does not exist." : exception.Message;
        }
    }

    private async Task LoadAsync()
    {
        _user = await Api.GetUserDetailAsync(Id);
        _permissions = await Api.GetEffectivePermissionsAsync(Id);
        (_firstName, _lastName, _active, _mustChangePassword) = (_user.FirstName, _user.LastName, _user.IsActive, _user.MustChangePassword);
        _roleIds = _user.Roles.Select(r => r.Id).ToList();
    }

    private static string DisplayName(UserDetailDto user) =>
        $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : user.Email;

    private static string Initials(UserDetailDto user) =>
        string.Concat(DisplayName(user).Split(' ', '@', '.').Where(p => p.Length > 0).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private Task SaveAsync() => RunAsync(() => Api.UpdateUserAsync(Id, new UpdateUserRequest(_firstName, _lastName, _active, _mustChangePassword)), "Saved.");

    private Task UnlockAsync() => RunAsync(() => Api.UnlockUserAsync(Id), "Unlocked.");

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

    private Task SendResetAsync() => RunAsync(() => Api.SendPasswordResetAsync(Id), "Password reset sent.");

    private async Task DeleteAsync()
    {
        if (!await Dialogs.ConfirmAsync("Delete user", $"Delete {_user!.Email} and all personal data? This cannot be undone.", "Delete", "Cancel", Icons.Material.Outlined.DeleteForever))
        {
            return;
        }

        try
        {
            await Api.DeleteUserAsync(Id);
            Snackbar.Add("User deleted.", Severity.Success);
            Nav.NavigateTo("/admin/users");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private Task SetRolesAsync(IReadOnlyCollection<Guid> ids) => RunAsync(() => Api.SetUserRolesAsync(Id, ids.ToList()), "Roles saved.");

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
            Snackbar.Add(exception.Message, Severity.Error);
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }
}
