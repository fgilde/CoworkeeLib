using ApexCharts;
using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting;

public sealed class ReportingOptions
{
    private readonly Dictionary<string, string> _pages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the report pages live, without slashes (default <c>reports</c>): the list, <c>{BasePath}/{id}</c>, <c>{BasePath}/new</c> and the links between
    /// them. An app that needs <c>/reports</c> for something else hosts the reports under another path.
    /// </summary>
    public string BasePath { get; set; } = "reports";

    /// <summary>Whether a report can be chosen as the user's or the organisation's dashboard (the start page of the Coworkee shell). Off in apps without it.</summary>
    public bool Dashboards { get; set; } = true;

    /// <summary>Whether reports can be subscribed by mail (the button on a report and the list's link to the subscriptions page).</summary>
    public bool Subscriptions { get; set; } = true;

    /// <summary>Whether a widget of an entity set without a page of its own (<see cref="DrillDown"/>) opens the module's generic data page. Off: only the registered pages.</summary>
    public bool DataPages { get; set; } = true;

    /// <summary>Chart colours instead of the theme's (primary, secondary, tertiary, ...); a widget's own palette still wins.</summary>
    public IReadOnlyList<string>? Colors { get; set; }

    /// <summary>Changes every chart's ApexCharts options after the module built them, e.g. square bars or solid grid lines for the look of another chart library.</summary>
    public Action<ApexChartOptions<ChartPoint>, WidgetOptions>? ConfigureChart { get; set; }

    /// <summary>The page with the data table of an entity set: drill-down opens it with the widget's filter as facet in the URL.</summary>
    public ReportingOptions DrillDown(string entitySet, string page)
    {
        _pages[entitySet] = page;
        return this;
    }

    /// <summary>Whether the app registered a page for the set or source; computed sources drill down only then.</summary>
    public bool HasPage(string entitySet) => _pages.ContainsKey(entitySet);

    /// <summary>Whether a widget of the set (a computed source when <paramref name="computed"/>) has a page to drill down to.</summary>
    public bool CanDrillDown(string entitySet, bool computed) => HasPage(entitySet) || (DataPages && !computed);

    /// <summary>The app's page for the set, otherwise the module's generic data page.</summary>
    public string PageFor(string entitySet) => _pages.GetValueOrDefault(entitySet) ?? Path($"data/{Uri.EscapeDataString(entitySet)}");

    /// <summary>A path below <see cref="BasePath"/>, relative to the app's base address: <c>Path()</c> is the list, <c>Path(id)</c> a report.</summary>
    public string Path(string? rest = null) => string.IsNullOrEmpty(rest) ? BasePath.Trim('/') : $"{BasePath.Trim('/')}/{rest}";
}
