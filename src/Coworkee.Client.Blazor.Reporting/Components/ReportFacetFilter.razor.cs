using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>
/// The seam to the library's facet UI: everything in reporting that picks facets goes through here. In: an entity set and a
/// <see cref="FacetSelection"/>; out: its OData filter. Counts come with the selection as facet context, so each "any of" group
/// counts without its own choice.
/// </summary>
public partial class ReportFacetFilter : IDisposable
{
    private readonly Dictionary<string, FacetView> _views = new(StringComparer.Ordinal);
    private IReadOnlyList<FacetGroupDto> _groups = [];
    private FacetSelection? _subscribed;
    private string? _loaded;

    /// <summary>The most values of a field without a facet that the bar offers.</summary>
    internal const int FieldValues = 1000;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private IReportsApi Reports { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    /// <summary>The other filters, so option counts match what the widgets show.</summary>
    [Parameter] public string? Filter { get; set; }

    /// <summary>The report's <see cref="ReportDefinition.FacetFields"/>: only these groups show, in this order; a field without a facet gets its values from a report query. Empty: every facet.</summary>
    [Parameter] public IReadOnlyList<string> FacetFields { get; set; } = [];

    /// <summary>The fields of the entity set, to label and query the fields without a facet.</summary>
    [Parameter] public IReadOnlyList<ReportFieldDto> Fields { get; set; } = [];

    /// <summary>Groups the filter bar shows for an earlier data set already, because both sets share them.</summary>
    [Parameter] public Func<FacetGroupDto, bool>? Hide { get; set; }

    /// <summary>The groups after each load, so the bar can tell which sets share a group.</summary>
    [Parameter] public EventCallback<IReadOnlyList<FacetGroupDto>> GroupsLoaded { get; set; }

    private IReadOnlyList<FacetGroupDto> Visible => Hide is null ? _groups : [.. _groups.Where(g => !Hide(g))];

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(_subscribed, Selection))
        {
            if (_subscribed is not null)
            {
                _subscribed.Changed -= SelectionChanged;
            }

            _subscribed = Selection;
            Selection.Changed += SelectionChanged;
        }

        await LoadAsync();
    }

    public void Dispose()
    {
        if (_subscribed is not null)
        {
            _subscribed.Changed -= SelectionChanged;
        }
    }

    private void SelectionChanged() => InvokeAsync(async () =>
    {
        await LoadAsync();
        StateHasChanged();
    });

    private async Task LoadAsync()
    {
        var key = $"{EntitySet}|{Filter}|{Selection.ToFilter()}|{string.Join(',', FacetFields)}";
        if (key == _loaded)
        {
            return;
        }

        _loaded = key;
        try
        {
            _groups = (await OData.QueryAsync<JsonElement>(EntitySet, new ODataQuery
            {
                Filter = ODataFilter.And(Filter, Selection.ToFilter()),
                Top = 1,
                Count = false,
                Facets = true,
                Select = "Id",
                FacetContext = Selection.Context(Filter, _groups),
            })).Facets;
        }
        catch (ApiException)
        {
            _groups = [];
        }

        if (FacetFields.Count > 0)
        {
            _groups = await ChosenAsync(_groups);
        }

        await GroupsLoaded.InvokeAsync(_groups);
    }

    // the report's fields in its order: the set's facet of a field, else the field's values counted by a report query (without the group's own choice)
    private async Task<IReadOnlyList<FacetGroupDto>> ChosenAsync(IReadOnlyList<FacetGroupDto> facets)
    {
        var chosen = new List<FacetGroupDto>();
        foreach (var name in FacetFields)
        {
            if (facets.FirstOrDefault(g => Is(g.Field, name) || Is(g.Key, name)) is { } facet)
            {
                chosen.Add(facet with { Order = chosen.Count });
            }
            else if (Fields.FirstOrDefault(f => Is(f.Path, name)) is { } field && await FieldGroupAsync(field, chosen.Count) is { } group)
            {
                chosen.Add(group);
            }
        }

        return chosen;
    }

    private async Task<FacetGroupDto?> FieldGroupAsync(ReportFieldDto field, int order)
    {
        var context = Selection.Context(Filter, _groups);
        var filter = context?.Without(field.Path) ?? Filter;
        try
        {
            var result = await Reports.QueryAsync(new ReportQueryRequest(EntitySet, filter, new ReportGroupBy(field.Path), Aggregates: [new ReportAggregate()], Top: FieldValues));
            var options = result.Rows.Where(r => r.Key is not null)
                .Select(r => new FacetOptionDto(JsonSerializer.SerializeToElement(r.Key!.Value), r.Key.Label, true, (int)(r.Values.FirstOrDefault() ?? 0), r.Key.Filter)).ToList();
            return new FacetGroupDto(field.Path, field.Label, field.Path, FacetKind.CheckboxList, FacetOperator.Or, options, true, order);
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
