using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The row total below a scrolling or grouped table; it shows a new count on its own, without rendering the rows again.</summary>
public partial class DataTableTotal
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public Func<long> Count { get; set; } = () => 0;

    internal void Refresh() => StateHasChanged();
}
