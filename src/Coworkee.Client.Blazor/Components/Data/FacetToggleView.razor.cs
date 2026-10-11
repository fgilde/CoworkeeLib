using System.Text.Json;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A yes/no facet as a three-way switch: all, yes or no, each with its count.</summary>
public partial class FacetToggleView
{
    private FacetOptionDto? Chosen => Group.Options.FirstOrDefault(IsSelected);

    private IEnumerable<FacetOptionDto> Ordered => Group.Options.OrderByDescending(IsYes);

    private static bool IsYes(FacetOptionDto option) => option.Value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => bool.TryParse(option.Value.GetString(), out var yes) && yes,
        _ => option.OData.EndsWith("true", StringComparison.OrdinalIgnoreCase),
    };
}
