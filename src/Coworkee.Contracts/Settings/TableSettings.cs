namespace Coworkee.Contracts.Settings;

public static class TableSettings
{
    /// <summary>The user's saved table views as JSON, keyed by table.</summary>
    public const string Views = "Table.Views";

    /// <summary>How tables without their own page mode show many rows: <see cref="Pager"/> or <see cref="EndlessScroll"/>.</summary>
    public const string DefaultPageMode = "DataTable.DefaultPageMode";

    public const string Pager = "Pager";

    public const string EndlessScroll = "EndlessScroll";

    /// <summary>The user's choice per table with a local copy, "Client" or "Server", as JSON keyed by table.</summary>
    public const string ClientEntityModes = "Table.ClientEntityModes";

    /// <summary>The user's view choices of report widgets as JSON, keyed by report and widget (see <c>ReportViews.Key</c>).</summary>
    public const string ReportViews = "Reports.Views";
}
