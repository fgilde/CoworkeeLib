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

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    private async Task CreateAsync()
    {
        if (await Dialogs.ShowEditAsync(L["New role"], new NewRole()) is { } role)
        {
            Guid id = default;
            if (await Snackbar.RunAsync(async () => id = await Api.CreateRoleAsync(new RoleRequest(role.Name, role.Description, role.SelectableForRegistration, role.GivenToNewUsers))))
            {
                Nav.NavigateTo(Links.Href($"/admin/roles/{id}")!);
            }
        }
    }

    private async Task EditAsync(RoleDto role)
    {
        // shared by every organisation and found by name; the API refuses it as well
        if (role.IsSystem)
        {
            Snackbar.Add(L["System roles cannot be changed."], Severity.Warning);
        }
        else if (await RoleForm.EditAsync(Dialogs, Api, L["Edit role"], role))
        {
            Snackbar.Add(L["Role saved"], Severity.Success);
        }
    }

    private async Task TransferAsync(RoleDto role)
    {
        if (await RoleUsersDialog.ShowAsync(Dialogs, L["Move or copy the users of {0}", role.Name], role) is not { } request)
        {
            return;
        }

        var count = 0;
        if (await Snackbar.RunAsync(async () => count = await Api.TransferRoleUsersAsync(role.Id, request)))
        {
            Snackbar.Add(L[request.Move ? "{0} users moved" : "{0} users copied", count], Severity.Success);
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

        public bool GivenToNewUsers { get; set; }
    }
}
