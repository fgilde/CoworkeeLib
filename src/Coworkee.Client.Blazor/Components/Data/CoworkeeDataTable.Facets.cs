using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    private const string AdvancedKey = "advanced";
    private readonly Dictionary<string, FacetView> _facetViews = new(StringComparer.Ordinal);
    private QueryGroup _advanced = new();
    private IReadOnlyList<QueryField>? _queryFields;
    private string? _lastAdvanced;
    private bool _advancedOpen;

    /// <summary>The page's say per facet group key: view, "any of / all of", exclusion, hiding. Wins over the server's [FacetBehavior].</summary>
    [Parameter] public IReadOnlyDictionary<string, FacetSettings>? FacetSettings { get; set; }

    /// <summary>Facets as pills above the rows or as a panel beside them.</summary>
    [Parameter] public FacetLayout FacetLayout { get; set; }

    /// <summary>A button that opens the query builder above the rows; defaults to <see cref="DataTableOptions.AdvancedFilter"/>.</summary>
    [Parameter] public bool? AdvancedFilter { get; set; }

    /// <summary>The fields of the query builder; by default the property columns and the reference facets.</summary>
    [Parameter] public IReadOnlyList<QueryField>? QueryFields { get; set; }

    /// <summary>The query of the advanced filter.</summary>
    public QueryGroup Advanced => _advanced;

    private bool ShowAdvanced => AdvancedFilter ?? Options.DataTable.AdvancedFilter;

    private bool PanelLayout => Facets && FacetLayout == FacetLayout.Panel;

    private IReadOnlyList<QueryField> Fields => QueryFields ?? (_grid is null ? [] : _queryFields ??= DataTableQueryFields.Of(_grid, _facets, L));

    private string? AdvancedFilterText => _advanced.IsEmpty ? null : ODataQueryTranslator.ToFilter(_advanced, Fields);

    private bool HasAdvanced => AdvancedFilterText is not null;

    private string? BaseFilter => ODataFilter.And(Filter, ColumnFilter, AdvancedFilterText, ODataFilter.Search(_search, SearchFields));

    private string L10n(string text) => L[text];

    private IReadOnlyList<FacetGroupDto> Shape(IReadOnlyList<FacetGroupDto> groups) => FacetSettings is null ? groups
        : [.. groups.Select(g => FacetSettings.TryGetValue(g.Key, out var settings) ? settings.Hidden ? null : settings.Apply(g) : g).OfType<FacetGroupDto>()];

    private void ShowFacets(IReadOnlyList<FacetGroupDto> groups)
    {
        _facets = Shape(groups);
        _queryFields = null;
    }

    private Task AdvancedChangedAsync(QueryGroup query)
    {
        var before = _lastAdvanced;
        _advanced = query;
        _lastAdvanced = AdvancedFilterText;
        if (before == _lastAdvanced)
        {
            return Task.CompletedTask;
        }

        WriteUrl();
        return ReloadAsync();
    }

    private void FacetViewsChanged()
    {
        WriteUrl();
        StateHasChanged();
    }

    private void RestoreFacets(DataTableState state)
    {
        _advanced = state.Advanced is { } advanced ? QueryJson.Clone(advanced) : new QueryGroup();
        _lastAdvanced = null;
        _facetViews.Clear();
        foreach (var (key, view) in state.FacetViews ?? new Dictionary<string, FacetView>())
        {
            _facetViews[key] = view;
        }
    }
}

public enum FacetLayout
{
    /// <summary>A row of pills above the rows, each opening its facet.</summary>
    Bar,

    /// <summary>A panel beside the rows with every facet open.</summary>
    Panel,
}
