using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class FacetChips
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;
}
