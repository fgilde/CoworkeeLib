using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

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
