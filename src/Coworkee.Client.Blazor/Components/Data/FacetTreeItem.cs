using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A node of <see cref="FacetTreeView"/>: a part of the values' path, the options ending here and the nodes below.</summary>
public sealed class FacetTreeItem(string name)
{
    public string Name { get; } = name;

    public List<FacetTreeItem> Children { get; } = [];

    public List<FacetOptionDto> Options { get; } = [];

    public IEnumerable<FacetOptionDto> Leaves() => Options.Concat(Children.SelectMany(c => c.Leaves()));
}
