using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>
/// The seam to the library's facet UI: everything in reporting that picks facets goes through here, so a new facet bar
/// (view modes, AND/OR per facet) or a query builder replaces this one file. In: an entity set and a <see cref="FacetSelection"/>; out: its OData filter.
/// </summary>
public partial class ReportFacetFilter
{
    private IReadOnlyList<FacetGroupDto> _groups = [];
    private string? _loaded;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    /// <summary>The other filters, so option counts match what the widgets show.</summary>
    [Parameter] public string? Filter { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var key = $"{EntitySet}|{Filter}";
        if (key == _loaded)
        {
            return;
        }

        _loaded = key;
        try
        {
            _groups = (await OData.QueryAsync<JsonElement>(EntitySet, new ODataQuery { Filter = Filter, Top = 1, Count = false, Facets = true, Select = "Id" })).Facets;
        }
        catch (ApiException)
        {
            _groups = [];
        }
    }
}
