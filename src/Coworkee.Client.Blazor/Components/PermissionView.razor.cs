using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class PermissionView
{
    [Parameter, EditorRequired] public string Permission { get; set; } = string.Empty;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public RenderFragment? NotAuthorized { get; set; }
}
