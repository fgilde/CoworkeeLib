using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Contracts.Identity;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>Name and description of a new or renamed group.</summary>
public sealed class GroupForm
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Asks for a new group under <paramref name="parentId"/> (a root for <c>null</c>); its id, or <c>null</c> when cancelled.</summary>
    public static async Task<Guid?> CreateAsync(IDialogService dialogs, ICoworkeeApi api, string title, Guid? parentId)
    {
        Guid? id = null;
        await dialogs.ShowEditAsync(title, new GroupForm(), async form => id = await api.CreateGroupAsync(new GroupRequest(form.Name, form.Description, parentId)));
        return id;
    }
}
