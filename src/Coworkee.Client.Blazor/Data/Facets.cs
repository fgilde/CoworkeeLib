using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>What a facet group shows and how it combines when neither the server nor the page said so.</summary>
public static class Facets
{
    private static readonly FacetView[] Lists = [FacetView.Checkbox, FacetView.Chips, FacetView.Select, FacetView.Buttons, FacetView.Swatches, FacetView.Tree];

    public static FacetCombine Combine(FacetGroupDto group) =>
        group.Combine ?? (!group.MultiSelect ? FacetCombine.Single : group.GroupOperator == FacetOperator.And ? FacetCombine.All : FacetCombine.Any);

    /// <summary>The view of a group: its own, else by value: yes/no as toggle, few enum values as chips, numbers as range, dates as presets, else a list.</summary>
    public static FacetView View(FacetGroupDto group, FacetView? chosen = null)
    {
        var view = chosen is { } c && Views(group).Contains(c) ? c : group.View;
        if (view != FacetView.Auto)
        {
            return view;
        }

        return Kind(group) switch
        {
            FacetValueKind.Boolean => FacetView.Toggle,
            FacetValueKind.Number when group.Type == FacetKind.Range => FacetView.Range,
            FacetValueKind.Date => FacetView.Dates,
            FacetValueKind.Enum or FacetValueKind.Flags when group.Options.Count <= 8 => FacetView.Chips,
            _ when group.Type == FacetKind.Radio && group.Options.Count <= 4 => FacetView.Buttons,
            _ => FacetView.Checkbox,
        };
    }

    /// <summary>The views a user may switch a group between.</summary>
    public static IReadOnlyList<FacetView> Views(FacetGroupDto group) => Kind(group) switch
    {
        FacetValueKind.Date => [FacetView.Dates, FacetView.Range],
        FacetValueKind.Number when group.Type == FacetKind.Range => [FacetView.Range],
        FacetValueKind.Boolean => [FacetView.Toggle, FacetView.Buttons, FacetView.Chips],
        _ => group.TreeSeparator is null ? [.. Lists.Where(v => v != FacetView.Tree)] : Lists,
    };

    public static FacetValueKind Kind(FacetGroupDto group) => group.ValueKind
        ?? (group.Type == FacetKind.DateRange ? FacetValueKind.Date : group.Type == FacetKind.Range ? FacetValueKind.Number : FacetValueKind.Text);

    /// <summary>Whether a group is a range (number or date) rather than a list of values.</summary>
    public static bool IsRange(FacetGroupDto group) => group.Type is FacetKind.Range or FacetKind.DateRange;
}

/// <summary>
/// The page's say on a facet, overriding the server: e.g. <c>["status"] = new() { View = FacetView.Buttons, AllowExclude = true }</c>
/// in <c>CoworkeeDataTable.FacetSettings</c>. Unset values keep the server's.
/// </summary>
public sealed record FacetSettings
{
    public FacetView? View { get; init; }

    public FacetCombine? Combine { get; init; }

    public bool? AllowExclude { get; init; }

    public bool? Switchable { get; init; }

    public string? TreeSeparator { get; init; }

    /// <summary>Hides the facet.</summary>
    public bool Hidden { get; init; }

    public FacetGroupDto Apply(FacetGroupDto group) => group with
    {
        View = View ?? group.View,
        Combine = Combine ?? group.Combine,
        AllowExclude = AllowExclude ?? group.AllowExclude,
        Switchable = Switchable ?? group.Switchable,
        TreeSeparator = TreeSeparator ?? group.TreeSeparator,
    };
}
