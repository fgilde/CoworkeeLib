using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>Name, description and parent of a group.</summary>
public partial class GroupGeneralTab
{
    private GroupRow? _shown;
    private string? _name;
    private string? _description;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public GroupRow Group { get; set; } = null!;

    /// <summary>The path of the parent ("Customer A / Hamburg"), <c>null</c> for a root.</summary>
    [Parameter] public string? Parent { get; set; }

    [Parameter] public bool CanManage { get; set; }

    [Parameter] public EventCallback Changed { get; set; }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_shown, Group))
        {
            (_shown, _name, _description) = (Group, Group.Name, Group.Description);
        }
    }

    private async Task SaveAsync()
    {
        if (await Snackbar.RunAsync(() => Api.UpdateGroupAsync(Group.Id, new GroupRequest(_name!, _description, Group.ParentId)), L["Group saved"]))
        {
            await Changed.InvokeAsync();
        }
    }

    private async Task SetDefaultAsync(bool on)
    {
        if (await Snackbar.RunAsync(() => Api.SetDefaultGroupAsync(on ? Group.Id : null)))
        {
            await Changed.InvokeAsync();
        }
    }

    private async Task MoveAsync()
    {
        if (await GroupMoveDialog.MoveAsync(Dialogs, Api, Snackbar, L, Group.Id, Group.Name))
        {
            await Changed.InvokeAsync();
        }
    }
}
