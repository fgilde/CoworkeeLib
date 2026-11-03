namespace Coworkee.Contracts.Reporting;

public sealed class WidgetQuery
{
    public string EntitySet { get; set; } = string.Empty;

    /// <summary>OData $filter of the widget itself, typed or from a query builder; <see cref="Facets"/> narrow it further.</summary>
    public string? Filter { get; set; }

    /// <summary>The query builder's rules as JSON, to edit them again; <see cref="Filter"/> holds their OData.</summary>
    public string? Rules { get; set; }

    /// <summary>The widget's own facets.</summary>
    public List<ReportFilterValue> Facets { get; set; } = [];

    /// <summary>Categories, e.g. the x axis.</summary>
    public ReportGroupBy? GroupBy { get; set; }

    /// <summary>Series within a category, e.g. stacked bars.</summary>
    public ReportGroupBy? SplitBy { get; set; }

    public List<ReportAggregate> Aggregates { get; set; } = [new()];

    /// <summary>The field the report's date range applies to.</summary>
    public string? DateField { get; set; }

    public ReportSortBy SortBy { get; set; }

    public bool Descending { get; set; }

    public int? Top { get; set; }

    /// <summary>Columns of tables, OData paths like "Customer/Name".</summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>The column tables sort by first, <see cref="Descending"/> or not.</summary>
    public string? OrderBy { get; set; }
}
