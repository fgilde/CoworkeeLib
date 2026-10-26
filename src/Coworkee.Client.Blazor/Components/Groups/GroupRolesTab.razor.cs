using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>The roles members get through a group.</summary>
public partial class GroupRolesTab
{
    private IReadOnlyList<RoleDto> _roles = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public GroupRow Group { get; set; } = null!;

    [Parameter] public bool CanManage { get; set; }

    [Parameter] public EventCallback Changed { get; set; }

    protected override Task OnInitializedAsync() => Snackbar.RunAsync(async () => _roles = await Api.GetRolesAsync());

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private async Task SetRolesAsync(IEnumerable<Guid> ids)
    {
        if (await Snackbar.RunAsync(() => Api.SetGroupRolesAsync(Group.Id, [.. ids])))
        {
            await Changed.InvokeAsync();
        }
    }
}
