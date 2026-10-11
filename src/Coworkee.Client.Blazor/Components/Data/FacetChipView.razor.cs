using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A facet as chips with counts, good for a handful of values such as statuses; a right click excludes a value where allowed.</summary>
public partial class FacetChipView
{
    private void ExcludeIfAllowed(FacetOptionDto option)
    {
        if (Group.AllowExclude)
        {
            Exclude(option);
        }
    }
}
