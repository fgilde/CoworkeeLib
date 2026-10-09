using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Roles
{
    private static readonly string[] SearchFields = [nameof(RoleDto.Name), nameof(RoleDto.Description)];
    private CoworkeeDataTable<RoleDto> _table = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    private async Task CreateAsync()
    {
        if (await Dialogs.ShowEditAsync(L["New role"], new NewRole()) is { } role)
        {
            Guid id = default;
            if (await Snackbar.RunAsync(async () => id = await Api.CreateRoleAsync(new RoleRequest(role.Name, role.Description, role.SelectableForRegistration))))
            {
                Nav.NavigateTo($"/admin/roles/{id}");
            }
        }
    }

    private async Task DeleteAsync(IReadOnlyCollection<RoleDto> roles)
    {
        foreach (var role in roles.Where(r => !r.IsSystem))
        {
            await Snackbar.RunAsync(() => Api.DeleteRoleAsync(role.Id));
        }
    }

    private sealed class NewRole
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool SelectableForRegistration { get; set; }
    }
}
