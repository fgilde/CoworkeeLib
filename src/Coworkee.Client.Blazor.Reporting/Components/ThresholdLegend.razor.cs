using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>The bands of a widget as text below a chart, for viewers who cannot tell the colours apart.</summary>
public partial class ThresholdLegend
{
    [Parameter, EditorRequired] public WidgetOptions Options { get; set; } = null!;
}
