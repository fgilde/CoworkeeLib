using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Title, description and actions of a page; <see cref="Title"/> and <see cref="Description"/> are text keys, translated here.</summary>
public partial class PageHeader
{
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    [Parameter] public string? Description { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }
}
