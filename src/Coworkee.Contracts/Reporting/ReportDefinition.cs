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
