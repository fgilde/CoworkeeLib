using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>
/// The widget's own filter: facets through <see cref="ReportFacetFilter"/> and a typed OData filter.
/// The second seam: a query builder replaces the text field and writes <see cref="WidgetQuery.Filter"/>.
/// </summary>
public partial class WidgetFilterEditor : IDisposable
{
    private FacetSelection _selection = new();
    private string? _set;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public WidgetQuery Query { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public EventCallback Changed { get; set; }

    protected override void OnParametersSet()
    {
        if (_set == Query.EntitySet && _selection.All.Count() == Query.Facets.Count)
        {
            return;
        }

        _set = Query.EntitySet;
        _selection.Changed -= Store;
        _selection = ReportState.Selection(Query.Facets);
        _selection.Changed += Store;
    }

    public void Dispose() => _selection.Changed -= Store;

    private Task FilterChanged(string? filter)
    {
        Query.Filter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        return Changed.InvokeAsync();
    }

    private void Store()
    {
        Query.Facets = [.. _selection.All.Select(f => new ReportFilterValue(Query.EntitySet, f.GroupKey, f.GroupLabel, f.Label, f.OData, f.Operator))];
        _ = InvokeAsync(() => Changed.InvokeAsync());
    }
}
