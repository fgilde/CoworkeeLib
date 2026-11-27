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

    /// <summary>Only rows with a value in <see cref="DateField"/> (<c>{DateField} ne null</c>), also without a date range.</summary>
    public bool RequireDate { get; set; }

    public ReportSortBy SortBy { get; set; }

    public bool Descending { get; set; }

    public int? Top { get; set; }

    /// <summary>Columns of tables, OData paths like "Customer/Name".</summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>
    /// The column tables sort by first, <see cref="Descending"/> or not. Grouped tables: a list like <c>"Total cost desc, Containers desc"</c> of group field paths
    /// and value labels (<see cref="ReportTableColumns"/>); entries naming no column are skipped.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>Grouped tables: the fields the rows are grouped by (1 to 8), each a column.</summary>
    public List<string> GroupFields { get; set; } = [];

    /// <summary>Grouped tables: values of other data sets grouped by the same <see cref="GroupFields"/>, as columns after the widget's own values.</summary>
    public List<WidgetJoin> Joins { get; set; } = [];
}
