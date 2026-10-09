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

    [Fact]
    public void The_admin_pages_sit_in_subgroups_and_the_administration_keeps_its_place()
    {
        var options = new NavigationMenuOptions().OrderGroup("Personal", 0).OrderGroup(NavigationGroups.Administration, 1);
        var tree = NavigationEntry.Build([.. new AdminNavigation().Items, new("Account", "/profile", Icons.Material.Outlined.Person, Group: "Personal")], options, null);

        tree.Where(e => e.Children is not null).Select(e => e.Text).ShouldBe(["Personal", NavigationGroups.Administration]);
        var admin = tree.Single(e => e.Text == NavigationGroups.Administration);
        admin.Children!.Select(e => e.Text).ShouldBe(["Identity", "System", "Communication", "Localization", "Monitoring"]);
        admin.Children!.Single(e => e.Text == "System").Children!.Select(e => e.Href).ShouldContain("/admin/services");
        new AdminNavigation().Items.Select(i => i.Href).ShouldNotContain("/admin/jobs");
    }
}
