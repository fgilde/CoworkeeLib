using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>The actions on one node of the group tree: new sub-group, rename, move and delete with its subtree.</summary>
public partial class GroupNodeMenu
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public GroupNodeDto Node { get; set; } = null!;

    /// <summary>A group was created, renamed or moved; its id.</summary>
    [Parameter] public EventCallback<Guid> Changed { get; set; }

    [Parameter] public EventCallback<GroupNodeDto> Deleted { get; set; }

    private async Task CreateAsync()
    {
        if (await GroupForm.CreateAsync(Dialogs, Api, L["New sub-group of {0}", Node.Name], Node.Id) is { } id)
        {
            await Changed.InvokeAsync(id);
        }
    }

    private async Task RenameAsync()
    {
        var form = new GroupForm { Name = Node.Name, Description = Node.Description };
        if (await Dialogs.ShowEditAsync(L["Rename group"], form, f => Api.UpdateGroupAsync(Node.Id, new GroupRequest(f.Name, f.Description, Node.ParentId))))
        {
            await Changed.InvokeAsync(Node.Id);
        }
    }

    private async Task MoveAsync()
    {
        if (await GroupMoveDialog.MoveAsync(Dialogs, Api, Snackbar, L, Node.Id, Node.Name))
        {
            await Changed.InvokeAsync(Node.Id);
        }
    }

    // ponytail: the counts are the direct sub-groups and members of the node; deeper levels would cost a query per level
    private async Task DeleteAsync()
    {
        var message = L["Delete {0} with its {1} sub-groups and all groups below them? {2} members lose this group. This cannot be undone.", Node.Name, Node.ChildCount, Node.MemberCount];
        if (await Dialogs.ConfirmAsync(L["Delete group"], message, L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever)
            && await Snackbar.RunAsync(() => Api.DeleteGroupAsync(Node.Id), L["Group deleted"]))
        {
            await Deleted.InvokeAsync(Node);
        }
    }
}
