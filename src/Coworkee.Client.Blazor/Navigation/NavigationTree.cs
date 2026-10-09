namespace Coworkee.Client.Blazor.Navigation;

public sealed record NavigationGroup(string Title, IReadOnlyList<CoworkeeNavItem> Items);

public static class NavigationTree
{
    public static IReadOnlyList<CoworkeeNavItem> Ungrouped(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options, string? filter = null) =>
        Visible(items, options, filter).Where(i => i.Group is null).OrderBy(i => i.Order).ToList();

    public static IReadOnlyList<NavigationGroup> Groups(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options, string? filter = null) =>
        Visible(items, options, filter)
            .Where(i => i.Group is not null)
            .GroupBy(i => i.Group!)
            .OrderBy(g => options.GroupOrder(Root(g.Key)))
            .ThenBy(g => Root(g.Key), StringComparer.CurrentCulture)
            .ThenBy(g => g.Key == Root(g.Key) ? 0 : 1)
            .ThenBy(g => options.GroupOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.CurrentCulture)
            .Select(g => new NavigationGroup(g.Key, g.OrderBy(i => i.Order).ToList()))
            .ToList();

    // a nested group sorts within its top level group, which keeps its own place
    private static string Root(string group) => group.Split(NavigationEntry.GroupSeparator)[0];

    private static IEnumerable<CoworkeeNavItem> Visible(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options, string? filter) =>
        items.Where(i => !options.Hidden.Contains(i.Href)
            && (string.IsNullOrWhiteSpace(filter)
                || i.Title.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || i.Group?.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase) == true));
}
