using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Moves an item of a list up or down, or removes it.</summary>
public partial class Reorder
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public int Index { get; set; }

    [Parameter] public int Count { get; set; }

    [Parameter] public EventCallback<int> Move { get; set; }

    [Parameter] public EventCallback Remove { get; set; }
}
