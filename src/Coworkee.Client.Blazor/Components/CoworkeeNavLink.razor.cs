using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeNavLink
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public CoworkeeNavItem Item { get; set; } = null!;
}
