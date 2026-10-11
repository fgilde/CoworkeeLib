using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

public partial class WidgetStatus
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public bool Loading { get; set; }

    [Parameter] public string? Error { get; set; }

    [Parameter] public string? Hint { get; set; }

    [Parameter] public bool Empty { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }
}
