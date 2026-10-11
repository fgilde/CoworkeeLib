using System.Text.Json.Serialization;
using Coworkee.Contracts.Data;

namespace Coworkee.Contracts.Reporting;

/// <summary>What a report shows: widgets on a 12 column grid, filters for all widgets of an entity set and a date range.</summary>
public sealed class ReportDefinition
{
    public const int GridColumns = 12;

    public List<ReportWidget> Widgets { get; set; } = [];

    /// <summary>Facets chosen in the report's filter bar; each applies to the widgets of its entity set.</summary>
    public List<ReportFilterValue> Filters { get; set; } = [];

    /// <summary>Applies to every widget with a date field.</summary>
    public ReportDateRange? DateRange { get; set; }

    /// <summary>Reloads the widgets every so many seconds; 0 keeps them until a change arrives.</summary>
    public int RefreshSeconds { get; set; }
}

public sealed record ReportFilterValue(string EntitySet, string GroupKey, string GroupLabel, string Label, string OData, FacetOperator Operator = FacetOperator.Or);

[JsonConverter(typeof(JsonStringEnumConverter<DateRangePreset>))]
public enum DateRangePreset
{
    Today,
    Last7Days,
    Last30Days,
    Last90Days,
    ThisMonth,
    LastMonth,
    ThisQuarter,
    ThisYear,
    LastYear,
    Last12Months,
    Custom,
}

public sealed record ReportDateRange(DateRangePreset Preset, DateOnly? From = null, DateOnly? To = null);

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
}

[JsonConverter(typeof(JsonStringEnumConverter<WidgetType>))]
public enum WidgetType
{
    Kpi,
    Chart,
    Table,
    DataTable,
    Pivot,
    Markdown,
    Image,
    Embed,
}

public sealed class WidgetQuery
{
    public string EntitySet { get; set; } = string.Empty;

    /// <summary>OData $filter of the widget itself, typed or from a query builder; <see cref="Facets"/> narrow it further.</summary>
    public string? Filter { get; set; }

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

public sealed record ReportGroupBy(string Field, DateBucket Bucket = DateBucket.None);

public sealed record ReportAggregate(AggregateFunction Function = AggregateFunction.Count, string? Field = null, string? Label = null);

[JsonConverter(typeof(JsonStringEnumConverter<DateBucket>))]
public enum DateBucket
{
    None,
    Day,
    Week,
    Month,
    Quarter,
    Year,
}

[JsonConverter(typeof(JsonStringEnumConverter<AggregateFunction>))]
public enum AggregateFunction
{
    Count,
    Sum,
    Average,
    Min,
    Max,
    DistinctCount,
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportSortBy>))]
public enum ReportSortBy
{
    Key,
    Value,
}
