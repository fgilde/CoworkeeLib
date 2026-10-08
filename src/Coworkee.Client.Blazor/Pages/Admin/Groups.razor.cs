using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Groups
{
    private static readonly string[] SearchFields = [nameof(GroupRow.Name), nameof(GroupRow.Description)];
    private CoworkeeDataTable<GroupRow> _table = null!;
    private IReadOnlyList<UserRow> _users = [];
    private IReadOnlyList<RoleDto> _roles = [];
    private bool _canManage;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        _canManage = await Permissions.HasAsync(IdentityPermissions.Groups.Manage);
        await Snackbar.RunAsync(async () =>
        {
            _users = (await OData.QueryAsync<UserRow>("Users", new ODataQuery { OrderBy = "Email", Top = 1000, Count = false })).Items;
            _roles = await Api.GetRolesAsync();
        });
    }

    private string UserName(Guid id) => _users.FirstOrDefault(u => u.Id == id)?.Email ?? string.Empty;

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private async Task CreateAsync()
    {
        if (await Dialogs.ShowEditAsync(L["New group"], new NewGroup()) is { } group)
        {
            await Snackbar.RunAsync(() => Api.CreateGroupAsync(new GroupRequest(group.Name, group.Description)));
        }
    }

    private async Task SetMembersAsync(GroupRow group, IEnumerable<Guid> ids)
    {
        await Snackbar.RunAsync(() => Api.SetGroupMembersAsync(group.Id, [.. ids]));
        await _table.ReloadAsync();
    }

    private async Task SetRolesAsync(GroupRow group, IEnumerable<Guid> ids)
    {
        await Snackbar.RunAsync(() => Api.SetGroupRolesAsync(group.Id, [.. ids]));
        await _table.ReloadAsync();
    }

    private async Task DeleteAsync(IReadOnlyCollection<GroupRow> groups)
    {
        foreach (var group in groups)
        {
            await Snackbar.RunAsync(() => Api.DeleteGroupAsync(group.Id));
        }
    }

    private sealed class NewGroup
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
    }
}
