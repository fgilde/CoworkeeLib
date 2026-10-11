namespace Coworkee.Client.Blazor.Reporting;

public sealed class ReportingOptions
{
    private readonly Dictionary<string, string> _pages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The page with the data table of an entity set: drill-down opens it with the widget's filter as facet in the URL.</summary>
    public ReportingOptions DrillDown(string entitySet, string page)
    {
        _pages[entitySet] = page;
        return this;
    }

    /// <summary>The app's page for the set, otherwise the module's generic data page.</summary>
    public string PageFor(string entitySet) => _pages.GetValueOrDefault(entitySet) ?? $"reports/data/{Uri.EscapeDataString(entitySet)}";
}
