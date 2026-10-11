using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>The widget's own filter: facets through <see cref="ReportFacetFilter"/> and rules of the query builder, kept as JSON with their OData.</summary>
public partial class WidgetFilterEditor : IDisposable
{
    private FacetSelection _selection = new();
    private QueryGroup _rules = new();
    private IReadOnlyList<QueryField> _fields = [];
    private string? _set;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public WidgetQuery Query { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public EventCallback Changed { get; set; }

    protected override void OnParametersSet()
    {
        if (_set == Query.EntitySet)
        {
            return;
        }

        _set = Query.EntitySet;
        _fields = ReportQueryFields.From(State.Source(Query.EntitySet));
        _rules = QueryJson.Deserialize(Query.Rules) ?? new QueryGroup();
        _selection.Changed -= Store;
        _selection = ReportState.Selection(Query.Facets);
        _selection.Changed += Store;
    }

    public void Dispose() => _selection.Changed -= Store;

    private Task RulesChanged(QueryGroup rules)
    {
        _rules = rules;
        Query.Filter = ODataQueryTranslator.ToFilter(rules, _fields);
        Query.Rules = Query.Filter is null ? null : QueryJson.Serialize(rules);
        return Changed.InvokeAsync();
    }

    private void Store()
    {
        Query.Facets = [.. _selection.All.Select(f => new ReportFilterValue(Query.EntitySet, f.GroupKey, f.GroupLabel, f.Label, f.OData, f.Operator))];
        _ = InvokeAsync(() => Changed.InvokeAsync());
    }
}
