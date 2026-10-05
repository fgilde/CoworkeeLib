using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Users
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private MudTable<UserDto>? _table;
    private string? _search;
    private bool _creating;
    private NewUser _new = new();
    private IReadOnlyList<RoleDto> _roles = [];

    protected override async Task OnInitializedAsync() => _roles = await Api.GetRolesAsync();

    private async Task<TableData<UserDto>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        var page = await Api.GetUsersAsync(new PageRequest(state.Page + 1, state.PageSize, _search), cancellationToken);
        return new TableData<UserDto> { Items = page.Items, TotalItems = page.TotalCount };
    }

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private async Task CreateAsync() => await RunAsync(async () =>
    {
        await Api.CreateUserAsync(new CreateUserRequest(_new.Email, _new.Password, _new.FirstName, _new.LastName));
        _new = new NewUser();
        _creating = false;
    });

    private Task SetRolesAsync(UserDto user, IEnumerable<Guid> ids) => RunAsync(() => Api.SetUserRolesAsync(user.Id, ids.ToList()));

    private Task SetActiveAsync(UserDto user, bool active) => RunAsync(() => Api.UpdateUserAsync(user.Id, new UpdateUserRequest(user.FirstName, user.LastName, active)));

    private UserDto? _permissionsOf;
    private IReadOnlyList<string> _permissions = [];

    private async Task ShowPermissionsAsync(UserDto user)
    {
        try
        {
            _permissions = await Api.GetEffectivePermissionsAsync(user.Id);
            _permissionsOf = user;
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task SendResetAsync(UserDto user)
    {
        try
        {
            await Api.SendPasswordResetAsync(user.Id);
            Snackbar.Add($"Password reset mail queued for {user.Email}.", Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }

        await _table!.ReloadServerData();
    }

    private sealed class NewUser
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string? FirstName { get; set; }

        public string? LastName { get; set; }
    }
}
