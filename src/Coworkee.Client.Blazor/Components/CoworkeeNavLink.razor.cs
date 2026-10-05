using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeNavLink
{
    [Parameter, EditorRequired] public CoworkeeNavItem Item { get; set; } = null!;
}
