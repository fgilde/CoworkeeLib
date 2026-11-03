using System.Text.Json;

namespace Coworkee.Contracts.Reporting;

public sealed class ReportWidget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public WidgetType Type { get; set; }

    public string? Title { get; set; }

    public string? Subtitle { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int W { get; set; } = 4;

    public int H { get; set; } = 4;

    public WidgetQuery? Query { get; set; }

    public WidgetOptions Options { get; set; } = new();

    /// <summary>Data an app or another client keeps with the widget, e.g. its own settings of it; reports neither read nor change it.</summary>
    public Dictionary<string, JsonElement>? AppData { get; set; }
}
