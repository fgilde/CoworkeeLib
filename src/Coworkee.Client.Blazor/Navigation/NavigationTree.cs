namespace Coworkee.Client.Blazor.Navigation;

public sealed record NavigationGroup(string Title, IReadOnlyList<CoworkeeNavItem> Items);

public static class NavigationTree
{
    public static IReadOnlyList<CoworkeeNavItem> Ungrouped(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options) =>
        Visible(items, options).Where(i => i.Group is null).OrderBy(i => i.Order).ToList();

    public static IReadOnlyList<NavigationGroup> Groups(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options) =>
        Visible(items, options)
            .Where(i => i.Group is not null)
            .GroupBy(i => i.Group!)
            .OrderBy(g => options.GroupOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.CurrentCulture)
            .Select(g => new NavigationGroup(g.Key, g.OrderBy(i => i.Order).ToList()))
            .ToList();

    private static IEnumerable<CoworkeeNavItem> Visible(IEnumerable<CoworkeeNavItem> items, NavigationMenuOptions options) =>
        items.Where(i => !options.Hidden.Contains(i.Href));
}
