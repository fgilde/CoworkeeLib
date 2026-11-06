using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class NavMiniModeTests : ClientTestBase
{
    public NavMiniModeTests()
    {
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Inbox", "/inbox", Icons.Material.Outlined.Inbox),
            new CoworkeeNavItem("Roles", "/admin/roles", Icons.Material.Outlined.Shield, Group: "Admin"),
            new CoworkeeNavItem("Users", "/admin/users", Icons.Material.Outlined.Person, Group: "Admin"),
            new CoworkeeNavItem("Texts", "/admin/texts", Icons.Material.Outlined.Translate, Group: "Admin/Localization"),
            new CoworkeeNavItem("Orders", "/sales/orders", Icons.Material.Outlined.ShoppingCart, Group: "Sales")));
        AddAuthorization().SetAuthorized("Ada");
    }

    private IRenderedComponent<MudPopoverProvider> _popovers = null!;

    private IRenderedComponent<CoworkeeNavMenu> Mini(CoworkeeTheme theme)
    {
        Services.GetRequiredService<ThemeService>().Preview(theme);
        _popovers = Render<MudPopoverProvider>();
        var menu = Render<CoworkeeNavMenu>(p => p.Add(m => m.Mini, true));
        menu.WaitForAssertion(() => menu.FindAll("[data-nav='/inbox']").ShouldNotBeEmpty());
        return menu;
    }

    [Fact]
    public void Every_link_shows_as_icon_with_a_line_before_each_group_of_the_first_level()
    {
        var menu = Mini(new CoworkeeTheme());

        menu.FindAll("[data-nav='/admin/roles']").ShouldNotBeEmpty();
        menu.FindAll("[data-nav='/admin/texts']").ShouldNotBeEmpty();
        menu.FindAll("[data-nav-divider]").Select(d => d.GetAttribute("data-nav-divider")).ShouldBe(["Admin", "Sales"]);
    }

    [Fact]
    public void The_lines_between_the_groups_can_be_switched_off()
    {
        var menu = Mini(new CoworkeeTheme { NavMiniDividers = false });

        menu.FindAll("[data-nav='/admin/roles']").ShouldNotBeEmpty();
        menu.FindAll("[data-nav-divider]").ShouldBeEmpty();
    }

    [Fact]
    public async Task Groups_mode_shows_the_first_level_and_opens_the_links_of_a_group_next_to_it_on_click()
    {
        var menu = Mini(new CoworkeeTheme { NavMiniMode = NavMiniMode.GroupsOnClick });
        var popovers = _popovers;

        menu.FindAll("[data-nav-group-button]").Select(b => b.GetAttribute("data-nav-group-button")).ShouldBe(["Admin", "Sales"]);
        menu.FindAll("[data-nav='/admin/roles']").ShouldBeEmpty();
        menu.Find("[data-nav-group-button='Admin']").GetAttribute("data-activation").ShouldBe("click");

        await menu.Find("[data-nav-group-button='Admin'] button").ClickAsync(new MouseEventArgs());

        popovers.WaitForAssertion(() => popovers.FindAll("[data-nav='/admin/roles']").ShouldNotBeEmpty());
        popovers.FindAll("[data-nav-group='Localization']").ShouldNotBeEmpty();
        await popovers.Find("[data-nav='/admin/users']").ClickAsync(new MouseEventArgs());
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/admin/users");
    }

    [Fact]
    public void Hover_mode_opens_a_group_when_pointing_at_it()
    {
        var menu = Mini(new CoworkeeTheme { NavMiniMode = NavMiniMode.GroupsOnHover });

        menu.Find("[data-nav-group-button='Sales']").GetAttribute("data-activation").ShouldBe("hover");
    }

    [Fact]
    public void The_full_menu_ignores_the_mini_mode()
    {
        Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { NavMiniMode = NavMiniMode.GroupsOnClick });

        var menu = Render<CoworkeeNavMenu>();

        menu.WaitForAssertion(() => menu.FindAll("[data-nav-group='Admin']").ShouldNotBeEmpty());
        menu.FindAll("[data-nav-group-button]").ShouldBeEmpty();
        menu.FindAll("[data-nav-divider]").ShouldBeEmpty();
    }
}
