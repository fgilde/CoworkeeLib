using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A facet as a list of check boxes (radio buttons for single choice) with counts, a search above long lists and "show more".</summary>
public partial class FacetCheckboxView
{
    private string? _search;
    private bool _all;

    /// <summary>Options shown before "show more"; chosen options always show.</summary>
    [Parameter] public int ShowFirst { get; set; } = 8;

    /// <summary>A search box appears above lists longer than this.</summary>
    [Parameter] public int SearchFrom { get; set; } = 8;

    private List<FacetOptionDto> Matching => [.. Group.Options.Where(o => string.IsNullOrWhiteSpace(_search) || Label(o).Contains(_search.Trim(), StringComparison.CurrentCultureIgnoreCase))];

    private IEnumerable<FacetOptionDto> Visible => _all || !string.IsNullOrWhiteSpace(_search)
        ? Matching
        : Matching.Where((o, i) => i < ShowFirst || IsSelected(o) || IsExcluded(o));

    private string Icon(FacetOptionDto option) => IsExcluded(option) ? Icons.Material.Outlined.DoNotDisturbOn
        : Single ? (IsSelected(option) ? Icons.Material.Filled.RadioButtonChecked : Icons.Material.Outlined.RadioButtonUnchecked)
        : IsSelected(option) ? Icons.Material.Filled.CheckBox : Icons.Material.Outlined.CheckBoxOutlineBlank;
}
