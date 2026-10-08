using Coworkee.Client.Blazor.Navigation;
using MudBlazor;
using Shouldly;

namespace Coworkee.Client.Blazor.Tests;

public sealed class NavigationEntryTests
{
    [Fact]
    public void Group_paths_nest_and_placed_links_move()
    {
        var options = new NavigationMenuOptions().Place("/admin/audit", "Personal", "Audit Trails");
        var tree = NavigationEntry.Build(
        [
            new("Users", "/admin/users", Icons.Material.Outlined.Person, Group: NavigationGroups.Administration),
            new("Languages", "/admin/languages", Icons.Material.Outlined.Language, Group: NavigationGroups.Localization),
            new("Audit log", "/admin/audit", Icons.Material.Outlined.History, Group: NavigationGroups.Administration),
        ], options, null);

        var admin = tree.Single(e => e.Text == NavigationGroups.Administration);
        admin.Children!.Select(e => e.Text).ShouldBe(["Users", "Localization"]);
        admin.Children!.Last().Children!.Single().Href.ShouldBe("/admin/languages");
        tree.Single(e => e.Text == "Personal").Children!.Single().Text.ShouldBe("Audit Trails");
    }
}
