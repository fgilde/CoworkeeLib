using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

public sealed record WidgetPreset(string Name, string Icon, WidgetType Type, ChartKind Chart, int W, int H);
