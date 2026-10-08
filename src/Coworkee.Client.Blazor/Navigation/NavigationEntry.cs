using Nextended.Core.Types;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>A node of the navigation tree: a group or a link.</summary>
public sealed class NavigationEntry : Hierarchical<NavigationEntry>
{
    public const char GroupSeparator = '/';

    public string Text { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public string? Href { get; set; }

    public bool ForceLoad { get; set; }

    public override string ToString() => Text;

    /// <summary>Groups become parents of their links, a group path like "Administration/Localization" nests; links without a group stay on the first level, home first.</summary>
    public static HashSet<NavigationEntry> Build(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options, string? homeTitle)
    {
        var arranged = items.Select(options.Arrange).ToList();
        var root = new NavigationEntry { Children = [] };
        if (homeTitle is not null)
        {
            root.Children.Add(new NavigationEntry { Text = homeTitle, Icon = MudBlazor.Icons.Material.Outlined.Home, Href = "/" });
        }

        foreach (var item in NavigationTree.Ungrouped(arranged, options))
        {
            root.Children.Add(Link(item, null));
        }

        var groups = new Dictionary<string, NavigationEntry>(StringComparer.Ordinal);
        foreach (var group in NavigationTree.Groups(arranged, options))
        {
            var parent = Group(group.Title, root, groups, options);
            foreach (var item in group.Items)
            {
                parent.Children!.Add(Link(item, parent));
            }
        }

        return root.Children;
    }

    private static NavigationEntry Group(string path, NavigationEntry root, Dictionary<string, NavigationEntry> groups, NavigationMenuOptions options)
    {
        if (groups.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var cut = path.LastIndexOf(GroupSeparator);
        var parent = cut < 0 ? root : Group(path[..cut], root, groups, options);
        var group = new NavigationEntry { Text = path[(cut + 1)..], Icon = options.GroupIcon(path), Children = [], Parent = cut < 0 ? null! : parent };
        parent.Children!.Add(group);
        return groups[path] = group;
    }

    private static NavigationEntry Link(CoworkeeNavItem item, NavigationEntry? parent) =>
        new() { Text = item.Title, Icon = item.Icon, Href = item.Href, ForceLoad = item.ForceLoad, Parent = parent! };
}
