using System.Globalization;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Base of the facet views: the group, the selection and how an option reads, is chosen or excluded.</summary>
public abstract class FacetViewBase : CoworkeeComponentBase
{
    [Inject] protected Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Parameter, EditorRequired] public FacetGroupDto Group { get; set; } = null!;

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    protected FacetCombine Combine => Selection.CombineOf(Group);

    protected bool Single => Combine == FacetCombine.Single;

    protected int MaxCount => Group.Options.Count == 0 ? 0 : Group.Options.Max(o => o.Count);

    protected bool IsSelected(FacetOptionDto option) => Selection.IsSelected(Group, option);

    protected bool IsExcluded(FacetOptionDto option) => Selection.IsExcluded(Group, option);

    protected bool IsDisabled(FacetOptionDto option) => !option.Enabled && option.Count == 0 && !IsSelected(option) && !IsExcluded(option);

    protected void Toggle(FacetOptionDto option) => Selection.Toggle(Group, option);

    protected void Exclude(FacetOptionDto option) => Selection.Toggle(Group, option, exclude: true);

    /// <summary>Enum, flag and yes/no labels are translated; names of rows (brands, customers) stay as they are.</summary>
    protected string Label(FacetOptionDto option) => FacetLabels.Of(L, Group, option);

    /// <summary>The label as the group's display renders it (a flag before a country), else as text.</summary>
    protected RenderFragment Value(FacetOptionDto option) =>
        FacetLabels.Render(Options.ValueRenderers, Group, option, Label(option));

    protected static string Count(int count) => count.ToString("N0", CultureInfo.CurrentCulture);

    protected string State(FacetOptionDto option) => IsExcluded(option) ? "excluded" : IsSelected(option) ? "selected" : string.Empty;

    /// <summary>Width of the bar under an option, its share of the largest count.</summary>
    protected string Share(FacetOptionDto option) => MaxCount == 0 ? "width:0" : FormattableString.Invariant($"width:{100.0 * option.Count / MaxCount:0.#}%");
}
