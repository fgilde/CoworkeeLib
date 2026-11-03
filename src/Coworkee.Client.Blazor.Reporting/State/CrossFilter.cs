namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>A click on a category of one widget that narrows the other widgets of its entity set.</summary>
public sealed record CrossFilter(string WidgetId, string EntitySet, string Label, string Filter);
