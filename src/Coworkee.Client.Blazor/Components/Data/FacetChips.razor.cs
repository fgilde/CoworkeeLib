using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class FacetChips
{
    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;
}
