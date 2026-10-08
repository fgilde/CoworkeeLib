using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class PageHeader
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    [Parameter] public string? Description { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }
}
