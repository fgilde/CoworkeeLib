using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class PermissionGate
{
    [Parameter] public string? Permission { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }
}
