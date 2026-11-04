using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// Holds the grid back while the table renders for something only outside the rows (facets, chips): a grid takes all its render fragments as changed
/// and renders every cell again whenever the table renders. The grid still renders itself when its rows load.
/// </summary>
public sealed class DataTableRowsGate : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public bool Hold { get; set; }

    protected override bool ShouldRender() => !Hold;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);
}
