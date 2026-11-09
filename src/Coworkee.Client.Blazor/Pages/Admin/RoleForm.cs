using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Contracts.Identity;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Name and description of an existing role.</summary>
public sealed class RoleForm
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Renames the role or changes its description; <c>true</c> when saved. The request leaves the flags null, so they keep their values.</summary>
    public static Task<bool> EditAsync(IDialogService dialogs, ICoworkeeApi api, string title, RoleDto role) =>
        dialogs.ShowEditAsync(title, new RoleForm { Name = role.Name, Description = role.Description },
            form => api.UpdateRoleAsync(role.Id, new RoleRequest(form.Name, form.Description)));
}
