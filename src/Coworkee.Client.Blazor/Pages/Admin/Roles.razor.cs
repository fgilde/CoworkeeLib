using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Roles
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<RoleDto> _roles = [];
    private string? _name;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync() => _roles = await Api.GetRolesAsync();

    private async Task CreateAsync()
    {
        try
        {
            await Api.CreateRoleAsync(new RoleRequest(_name!, null));
            _name = null;
            _roles = await Api.GetRolesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task DeleteAsync(RoleDto role)
    {
        try
        {
            await Api.DeleteRoleAsync(role.Id);
            _roles = await Api.GetRolesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
}
