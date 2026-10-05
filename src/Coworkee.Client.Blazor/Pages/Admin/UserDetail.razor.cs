using Coworkee.Client.Blazor.Api;
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
        (_firstName, _lastName, _active) = (_user.FirstName, _user.LastName, _user.IsActive);
        _roleIds = _user.Roles.Select(r => r.Id).ToList();
    }

    private static string DisplayName(UserDetailDto user) =>
        $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : user.Email;

    private static string Initials(UserDetailDto user) =>
        string.Concat(DisplayName(user).Split(' ', '@', '.').Where(p => p.Length > 0).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private Task SaveAsync() => RunAsync(() => Api.UpdateUserAsync(Id, new UpdateUserRequest(_firstName, _lastName, _active)), "Saved.");

    private Task UnlockAsync() => RunAsync(() => Api.UnlockUserAsync(Id), "Unlocked.");

    private Task SendResetAsync() => RunAsync(() => Api.SendPasswordResetAsync(Id), "Password reset sent.");

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
