using Nextended.Core.Types;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>A node of the navigation tree: a group or a link.</summary>
public sealed class NavigationEntry : Hierarchical<NavigationEntry>
{
    public string Text { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public string? Href { get; set; }

    public bool ForceLoad { get; set; }

    public override string ToString() => Text;

    /// <summary>Groups become parents of their links; links without a group stay on the first level, home first.</summary>
    public static HashSet<NavigationEntry> Build(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options, string? homeTitle)
    {
        var tree = new HashSet<NavigationEntry>();
        if (homeTitle is not null)
        {
            tree.Add(new NavigationEntry { Text = homeTitle, Icon = MudBlazor.Icons.Material.Outlined.Home, Href = "/" });
        }

        foreach (var item in NavigationTree.Ungrouped(items, options))
        {
            tree.Add(Link(item, null));
        }

        foreach (var group in NavigationTree.Groups(items, options))
        {
            var parent = new NavigationEntry { Text = group.Title, Icon = options.GroupIcon(group.Title) };
            parent.Children = [.. group.Items.Select(i => Link(i, parent))];
            tree.Add(parent);
        }

        return tree;
    }

    private static NavigationEntry Link(CoworkeeNavItem item, NavigationEntry? parent) =>
        new() { Text = item.Title, Icon = item.Icon, Href = item.Href, ForceLoad = item.ForceLoad, Parent = parent! };
}
