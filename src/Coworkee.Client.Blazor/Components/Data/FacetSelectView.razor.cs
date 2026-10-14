using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A facet as a dropdown, compact for long lists: multi-select for "any of" and "all of", a single choice otherwise.</summary>
public partial class FacetSelectView
{
    private IReadOnlyCollection<string> Chosen => [.. Group.Options.Where(IsSelected).Select(o => o.OData)];

    // one binding per mode: a multi-select also answers a changed SelectedValues through ValueChanged, which undid choices made elsewhere (the column filter)
    private Dictionary<string, object?> Binding => Single
        ? new()
        {
            [nameof(MudSelect<string>.Value)] = Chosen.FirstOrDefault(),
            [nameof(MudSelect<string>.ValueChanged)] = EventCallback.Factory.Create<string?>(this, v => Choose(v is null ? [] : [v])),
        }
        : new()
        {
            [nameof(MudSelect<string>.MultiSelection)] = true,
            [nameof(MudSelect<string>.SelectedValues)] = Chosen,
            [nameof(MudSelect<string>.SelectedValuesChanged)] = EventCallback.Factory.Create<IReadOnlyCollection<string?>?>(this, Choose),
            [nameof(MudSelect<string>.MultiSelectionTextFunc)] = new Func<IReadOnlyList<string?>?, string>(Text),
        };

    private string Text(IReadOnlyList<string?>? values) =>
        string.Join(", ", (values ?? []).Select(v => Group.Options.FirstOrDefault(o => o.OData == v) is { } option ? Label(option) : v));

    private void Choose(IEnumerable<string?>? values)
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
