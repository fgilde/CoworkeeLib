namespace Coworkee.Contracts.Reporting;

/// <summary>Values of another data set next to a grouped table's own, grouped by the table's fields; the set needs each of them.</summary>
public sealed class WidgetJoin
{
    public string EntitySet { get; set; } = "";

    public List<ReportAggregate> Aggregates { get; set; } = [];

    /// <summary>The widget's query as it narrows the joined set: the widget's filter, facets and date field apply to it too, so the set needs their fields as well.</summary>
    public WidgetQuery QueryFor(WidgetQuery widget) => new()
    {
        EntitySet = EntitySet, Filter = widget.Filter, Facets = widget.Facets, DateField = widget.DateField, RequireDate = widget.RequireDate, Aggregates = Aggregates,
    };
}
