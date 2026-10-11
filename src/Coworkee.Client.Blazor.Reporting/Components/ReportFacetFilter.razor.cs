using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
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

    [Inject] private IODataClient OData { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    /// <summary>The other filters, so option counts match what the widgets show.</summary>
    [Parameter] public string? Filter { get; set; }

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
        var key = $"{EntitySet}|{Filter}|{Selection.ToFilter()}";
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
    }
}
