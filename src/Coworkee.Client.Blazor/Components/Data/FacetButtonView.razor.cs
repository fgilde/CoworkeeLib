namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A facet as a segmented row of buttons, for two to five values; single choice adds "All" in front.</summary>
public partial class FacetButtonView
{
    private void Clear() => Selection.Only(Group, null);
}
