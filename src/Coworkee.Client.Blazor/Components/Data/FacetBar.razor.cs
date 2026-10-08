using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class FacetBar
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<FacetGroupDto> Groups { get; set; } = [];

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    private int SelectedCount(FacetGroupDto group) => Selection.All.Count(f => f.GroupKey == group.Key);
}
