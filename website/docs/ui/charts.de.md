# Kleine Diagramme

Drei kleine Diagramm-Komponenten für Kacheln, Karten und Seitenleisten, ohne das Reporting-Modul: reines HTML und SVG, Theme-Farben (`--mud-palette-*`, damit funktionieren Dark Mode und eigene Themes ohne Weiteres), kein JavaScript, und die Zahlen bleiben lesbarer Text für Screenreader.

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

`ChartValue(Label, Value, Color?)` ist ein Balken oder Segment; ohne Farbe wird die Theme-Palette der Reihe nach verwendet (Primary, Secondary, Tertiary, Info, Success, Warning, Error). `Color` nimmt jede CSS-Farbe, auch `var(--mud-palette-success)`.

| Komponente | Parameter |
|---|---|
| `CoworkeeBarChart` | `Values`, `Title`, `Vertical` (Säulen statt Zeilen), `Height` (vertikal, Standard `180px`), `Max` (Wert eines vollen Balkens; ohne Angabe der größte Wert), `ShowPercent` (Anteil an der Summe hinter dem Wert), `Format` (Standard `#,0.##`). |
| `CoworkeeDonut` | `Values`, `Title`, `CenterLabel`, `CenterSubLabel` oder `Center` (eigenes Markup), `Size` (Standard `140px`), `Thickness` (Standard `5` einer 42 breiten View-Box), `ShowLegend` (Standard an), `ShowPercent` (Standard an), `Format`. Werte von null oder kleiner zeichnen kein Segment. |
| `CoworkeeSparkline` | `Values` (`IReadOnlyList<double>`), `Title`, `Color` (Standard Primary), `Fill` (zarte Fläche unter der Linie), `Width` (`100px`), `Height` (`28px`), `Format`. |

Alle drei nehmen `Class`, `Style` und beliebige weitere Attribute.

**Barrierefreiheit:** Das Balkendiagramm ist eine `figure` mit einer Liste aus Beschriftung und Wert; die Balken selbst sind `aria-hidden`. Der Donut behält seine Legende für Screenreader, auch bei `ShowLegend="false"` (nur unsichtbar). Die Sparkline ist ein `img`, dessen Label aus Titel, Wertebereich und letztem Wert besteht (*Sick days per week: 2 – 9, 4*).

## Wann etwas anderes passt

- Achsen, mehrere Reihen, Zeitachsen, Zoom, Export: das Modul [Berichte](../modules/reporting.md) (ApexCharts) oder `MudChart` aus MudBlazor (`ChartType.Bar`, `Line`, `Donut`, `Pie`, `StackedBar`, `HeatMap`, ...), das mit MudBlazor schon da ist. `MudChart` zeichnet nur senkrechte Balken und hat keine eigene Textalternative; für die kleinen Fälle oben brauchen diese Komponenten weniger Einrichtung und lesen sich besser.
