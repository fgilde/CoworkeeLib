namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>A click on a category of one widget that narrows the other widgets of its entity set, and those of other sets that have the <paramref name="Field"/> too.</summary>
public sealed record CrossFilter(string WidgetId, string EntitySet, string Label, string Filter, string? Field = null);
