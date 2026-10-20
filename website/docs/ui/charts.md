# Small charts

Three small chart components for tiles, cards and side panels, without the Reporting module: plain HTML and SVG, theme colours (`--mud-palette-*`, so dark mode and custom themes just work), no JavaScript, and the numbers stay readable text for screen readers.

```razor
@using Coworkee.Client.Blazor.Components.Charts

<CoworkeeBarChart Title="Absences this year" Values="_byType" ShowPercent />
<CoworkeeBarChart Title="Hires per quarter" Values="_hires" Vertical Height="160px" />
<CoworkeeDonut Title="Requests" Values="_status" CenterLabel="@_open.ToString()" CenterSubLabel="open" />
<CoworkeeSparkline Title="Sick days per week" Values="_perWeek" Fill />

@code {
    private readonly ChartValue[] _byType = [new("Vacation", 112), new("Sick", 31, "#ef4444"), new("Training", 12)];
}
```

`ChartValue(Label, Value, Color?)` is a bar or segment; without a colour the theme palette is used in order (primary, secondary, tertiary, info, success, warning, error). `Color` takes any CSS colour, also `var(--mud-palette-success)`.

| Component | Parameters |
|---|---|
| `CoworkeeBarChart` | `Values`, `Title`, `Vertical` (columns instead of rows), `Height` (vertical, default `180px`), `Max` (value of a full bar; the largest value when not set), `ShowPercent` (share of the total after the value), `Format` (default `#,0.##`). |
| `CoworkeeDonut` | `Values`, `Title`, `CenterLabel`, `CenterSubLabel` or `Center` (own markup), `Size` (default `140px`), `Thickness` (default `5` of a 42 wide view box), `ShowLegend` (default on), `ShowPercent` (default on), `Format`. Values of zero or less draw no segment. |
| `CoworkeeSparkline` | `Values` (`IReadOnlyList<double>`), `Title`, `Color` (default primary), `Fill` (soft area under the line), `Width` (`100px`), `Height` (`28px`), `Format`. |

All three take `Class`, `Style` and any other attribute.

**Accessibility:** the bar chart is a `figure` with a list of label and value; the bars themselves are `aria-hidden`. The donut keeps its legend for screen readers when `ShowLegend="false"` (visually hidden). The sparkline is an `img` whose label is the title, the range and the last value (*Sick days per week: 2 – 9, 4*).

## When to use something else

- Axes, several series, time lines, zoom, export: the [Reporting](../modules/reporting.md) module (ApexCharts) or MudBlazor's `MudChart` (`ChartType.Bar`, `Line`, `Donut`, `Pie`, `StackedBar`, `HeatMap`, ...), which comes with MudBlazor. `MudChart` draws vertical bars only and has no text alternative of its own; for the small cases above these components need less setup and read better.
