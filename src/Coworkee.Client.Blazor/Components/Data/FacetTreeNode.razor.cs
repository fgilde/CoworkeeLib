using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>One node of <see cref="FacetTreeView"/> and, when open, the nodes below it; a node with a choice below starts open.</summary>
public partial class FacetTreeNode
{
    private bool? _expanded;

    [Parameter, EditorRequired] public FacetTreeItem Node { get; set; } = null!;

    [Parameter, EditorRequired] public FacetTreeView View { get; set; } = null!;

    [Parameter] public int Depth { get; set; }

    private bool Expanded => _expanded ?? View.StateOf(Node) is null;

    private string Icon => View.StateOf(Node) switch
    {
        true => Icons.Material.Filled.CheckBox,
        null => Icons.Material.Filled.IndeterminateCheckBox,
        _ => Icons.Material.Outlined.CheckBoxOutlineBlank,
    };
}
