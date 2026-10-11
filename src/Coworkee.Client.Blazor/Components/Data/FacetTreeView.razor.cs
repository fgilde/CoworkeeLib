using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// A facet as a tree: values split at the group's tree separator ("Europe / Germany / Berlin"). A node chooses every value below it;
/// it shows as chosen when all are, half chosen when some are.
/// </summary>
public partial class FacetTreeView
{
    private List<FacetTreeItem> Roots
    {
        get
        {
            var separator = Group.TreeSeparator ?? "/";
            var roots = new List<FacetTreeItem>();
            foreach (var option in Group.Options)
            {
                var level = roots;
                FacetTreeItem? node = null;
                foreach (var part in Label(option).Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    node = level.FirstOrDefault(n => n.Name == part) ?? Add(level, part);
                    level = node.Children;
                }

                node?.Options.Add(option);
            }

            return roots;
        }
    }

    internal bool? StateOf(FacetTreeItem node)
    {
        var leaves = node.Leaves().ToList();
        var chosen = leaves.Count(IsSelected);
        return chosen == 0 ? false : chosen == leaves.Count ? true : null;
    }

    internal void ToggleNode(FacetTreeItem node) => Selection.Choose(Group, node.Leaves(), StateOf(node) != true);

    internal static string CountOf(FacetTreeItem node) => Count(node.Leaves().Sum(o => o.Count));

    private static FacetTreeItem Add(List<FacetTreeItem> level, string name)
    {
        var node = new FacetTreeItem(name);
        level.Add(node);
        return node;
    }
}

/// <summary>A node of <see cref="FacetTreeView"/>: a part of the values' path, the options ending here and the nodes below.</summary>
public sealed class FacetTreeItem(string name)
{
    public string Name { get; } = name;

    public List<FacetTreeItem> Children { get; } = [];

    public List<FacetOptionDto> Options { get; } = [];

    public IEnumerable<FacetOptionDto> Leaves() => Options.Concat(Children.SelectMany(c => c.Leaves()));
}
