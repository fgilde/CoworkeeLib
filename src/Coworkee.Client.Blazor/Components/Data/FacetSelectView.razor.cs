namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A facet as a dropdown, compact for long lists: multi-select for "any of" and "all of", a single choice otherwise.</summary>
public partial class FacetSelectView
{
    private IReadOnlyCollection<string> Chosen => [.. Group.Options.Where(IsSelected).Select(o => o.OData)];

    private string Text(IReadOnlyList<string> values) =>
        string.Join(", ", values.Select(v => Group.Options.FirstOrDefault(o => o.OData == v) is { } option ? Label(option) : v));

    private void Choose(IEnumerable<string>? values)
    {
        var wanted = values?.ToHashSet() ?? [];
        if (Single)
        {
            Selection.Only(Group, Group.Options.FirstOrDefault(o => wanted.Contains(o.OData)));
            return;
        }

        foreach (var option in Group.Options.Where(o => wanted.Contains(o.OData) != IsSelected(o)).ToList())
        {
            Toggle(option);
        }
    }
}
