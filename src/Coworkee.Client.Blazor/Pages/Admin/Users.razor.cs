using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Users
{
    private static readonly string[] SearchFields = [nameof(UserRow.Email), nameof(UserRow.FirstName), nameof(UserRow.LastName)];
    private CoworkeeDataTable<UserRow> _table = null!;
    private IReadOnlyList<RoleDto> _roles = [];
    private IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>> _userRoles = new Dictionary<Guid, IReadOnlyList<RoleRefDto>>();
    private bool _creating;
    private NewUser _new = new();
    private UserRow? _permissionsOf;
    private IReadOnlyList<string> _permissions = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync() => await Snackbar.RunAsync(async () => _roles = await Api.GetRolesAsync());

    private async Task LoadRolesAsync(IReadOnlyList<UserRow> users) =>
        await Snackbar.RunAsync(async () => _userRoles = await Api.GetUsersRolesAsync([.. users.Select(u => u.Id)]));

    private IReadOnlyCollection<Guid> RolesOf(UserRow user) => [.. _userRoles.GetValueOrDefault(user.Id)?.Select(r => r.Id) ?? []];

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private async Task CreateAsync()
    {
        if (await Snackbar.RunAsync(() => Api.CreateUserAsync(new CreateUserRequest(_new.Email, _new.Password, _new.FirstName, _new.LastName)), L["User created"]))
        {
            (_new, _creating) = (new NewUser(), false);
            await _table.ReloadAsync();
        }
    }

    private async Task SetRolesAsync(UserRow user, IEnumerable<Guid> ids)
    {
        await Snackbar.RunAsync(() => Api.SetUserRolesAsync(user.Id, [.. ids]));
        await _table.ReloadAsync();
    }

    private async Task SetActiveAsync(UserRow user, bool active)
    {
        await Snackbar.RunAsync(() => Api.UpdateUserAsync(user.Id, new UpdateUserRequest(user.FirstName, user.LastName, active)));
        await _table.ReloadAsync();
    }

    private async Task ShowPermissionsAsync(UserRow user)
    {
        if (await Snackbar.RunAsync(async () => _permissions = await Api.GetEffectivePermissionsAsync(user.Id)))
        {
            _permissionsOf = user;
        }
    }

    private Task SendResetAsync(UserRow user) => Snackbar.RunAsync(() => Api.SendPasswordResetAsync(user.Id), L["Password reset mail queued for {0}.", user.Email]);

    private sealed class NewUser
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string? FirstName { get; set; }

        public string? LastName { get; set; }
    }
}
