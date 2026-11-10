using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The managed header: the administrator's order, placement and hidden items of the app bar (setting <see cref="HeaderSettings.Items"/>).</summary>
public sealed class HeaderTests : ClientTestBase
{
    public HeaderTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Services.AddSingleton<IAppBarContributor>(new Bar());
        AddAuthorization().SetAuthorized("Ada");
    }

    private void Configure(string? json, string? position = null, string? second = null) =>
        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?>
        {
            [HeaderSettings.Items] = json, [HeaderSettings.Position] = position, [HeaderSettings.SecondPosition] = second,
        });

    private IRenderedComponent<CoworkeeLayout> RenderLayout() =>
        Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

    private static HeaderItem Item(string id, int order) => new(id, id, string.Empty, order);

    [Fact]
    public void The_configured_order_wins_and_new_items_join_after_their_default_neighbour()
    {
        HeaderItem[] available = [Item("a", 1), Item("b", 2), Item("new", 3), Item("c", 4)];
        HeaderItemSetting[] configured = [new("c", HeaderPlacement.Bar), new("gone", HeaderPlacement.Bar), new("b", HeaderPlacement.Hidden), new("a", HeaderPlacement.Overflow)];

        var arranged = HeaderItems.Arrange(available, configured);

        arranged.Select(i => i.Item.Id).ShouldBe(["c", "b", "new", "a"]);
        arranged.Select(i => i.Placement).ShouldBe([HeaderPlacement.Bar, HeaderPlacement.Hidden, HeaderPlacement.Bar, HeaderPlacement.Overflow]);
        // without a configuration the default order
        HeaderItems.Arrange(available, []).Select(i => i.Item.Id).ShouldBe(["a", "b", "new", "c"]);
    }

    [Fact]
    public void The_account_menu_cannot_be_hidden_and_broken_settings_fall_back_to_the_defaults()
    {
        HeaderItems.Arrange([Item(HeaderItems.User, 1)], HeaderItems.Parse("""[{"id":"user","placement":"Hidden"}]""")).Single().Placement.ShouldBe(HeaderPlacement.Bar);
        HeaderItems.Parse("not json").ShouldBeEmpty();
        HeaderItems.Parse(null).ShouldBeEmpty();
        var settings = new[] { new HeaderItemSetting("about", HeaderPlacement.Overflow) };
        HeaderItems.Parse(HeaderItems.Serialize(settings)).ShouldBe(settings);
    }

    [Fact]
    public void Permissions_and_their_match_round_trip_and_are_left_out_when_there_are_none()
    {
        var parsed = HeaderItems.Parse("""[{"id":"about","placement":"Overflow","permissions":["A","B"],"match":"All"},{"id":"rtl","placement":"Bar"}]""");

        parsed[0].Permissions.ShouldBe(["A", "B"]);
        parsed[0].Match.ShouldBe(PermissionMatch.All);
        // settings saved before items could be restricted have no restriction
        parsed[1].Permissions.ShouldBeNull();
        parsed[1].Match.ShouldBe(PermissionMatch.Any);
        var json = HeaderItems.Serialize(parsed);
        json.ShouldBe("""[{"id":"about","placement":"Overflow","permissions":["A","B"],"match":"All"},{"id":"rtl","placement":"Bar"}]""");
        HeaderItems.Serialize([new HeaderItemSetting("about", HeaderPlacement.Bar, ["A"])]).ShouldBe("""[{"id":"about","placement":"Bar","permissions":["A"]}]""");
    }

    [Fact]
    public async Task Restricted_items_show_only_with_any_or_all_of_their_permissions_and_follow_changes_of_them()
    {
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs(["A"], ["A", "B"]);
        Configure("""
            [{"id":"about","placement":"Bar","permissions":["A","B"],"match":"All"},{"id":"rtl","placement":"Bar","permissions":["A","B"]},
             {"id":"user","placement":"Bar","permissions":["X"]}]
            """);

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find("[data-testid='rtl-toggle']"));
        layout.FindAll("[data-testid='about-button']").ShouldBeEmpty();
        // nobody can be locked out of signing out
        layout.Find("[data-testid='user-menu']");

        await layout.InvokeAsync(Services.GetRequiredService<PermissionStore>().Reset);

        layout.WaitForAssertion(() => layout.Find("[data-testid='about-button']"));
    }

    [Fact]
    public void App_items_get_an_id_from_their_component_unless_they_name_one()
    {
        new AppBarItem(typeof(LayoutCustomizationTests.LanguageSwitch)).Id.ShouldBe(nameof(LayoutCustomizationTests.LanguageSwitch));
        new AppBarItem(typeof(LayoutCustomizationTests.LanguageSwitch), Id: "groups", Title: "Groups").Title.ShouldBe("Groups");
    }

    [Fact]
    public void The_app_bar_follows_the_configured_order_and_hides_items()
    {
        Configure("""[{"id":"about","placement":"Bar"},{"id":"search","placement":"Bar"},{"id":"rtl","placement":"Hidden"}]""");

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.FindAll("[data-testid='rtl-toggle']").ShouldBeEmpty());
        var bar = layout.Find(".mud-appbar").InnerHtml;
        bar.IndexOf("about-button", StringComparison.Ordinal).ShouldBeLessThan(bar.IndexOf("palette-button", StringComparison.Ordinal));
        // the app's item is not configured: it keeps its default place after the search
        bar.IndexOf("palette-button", StringComparison.Ordinal).ShouldBeLessThan(bar.IndexOf("language-switch", StringComparison.Ordinal));
        layout.FindAll("[data-testid='app-bar-more']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Items_in_the_overflow_menu_show_there_and_the_search_keeps_its_shortcut()
    {
        Configure("""[{"id":"about","placement":"Overflow"},{"id":"search","placement":"Overflow"}]""");

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar-more']"));
        layout.FindAll("[data-testid='about-button']").ShouldBeEmpty();
        layout.FindAll("[data-testid='palette-button']").ShouldBeEmpty();
        await layout.Find("[data-testid='app-bar-more'] button").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='about-button']"));
        await layout.Find("[data-testid='header-search']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='palette']"));
    }

    [Fact]
    public async Task The_role_switch_is_an_item_of_its_own_that_can_move_to_the_overflow_menu_or_be_hidden()
    {
        MyRoleDto[] roles = [new(Guid.NewGuid(), "Sales", null, true), new(Guid.NewGuid(), "Support", null, true)];
        Api.GetMyRolesAsync(null, Arg.Any<CancellationToken>()).Returns(new MyRolesDto(true, null, roles));
        Configure(null);
        var layout = RenderLayout();
        // by default in the bar, before the account menu and no longer inside it
        layout.WaitForAssertion(() => layout.Find("[data-testid='active-role']"));
        var bar = layout.Find(".mud-appbar").InnerHtml;
        bar.IndexOf("active-role", StringComparison.Ordinal).ShouldBeLessThan(bar.IndexOf("user-menu", StringComparison.Ordinal));

        Configure("""[{"id":"role","placement":"Overflow"}]""");
        await layout.InvokeAsync(Services.GetRequiredService<ThemeService>().LoadAsync);
        layout.WaitForAssertion(() => layout.FindAll(".mud-appbar [data-testid='active-role']").ShouldBeEmpty());
        await layout.Find("[data-testid='app-bar-more'] button").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='active-role-menu']"));

        Configure("""[{"id":"role","placement":"Hidden"}]""");
        await layout.InvokeAsync(Services.GetRequiredService<ThemeService>().LoadAsync);
        layout.WaitForAssertion(() => layout.FindAll("[data-testid='active-role-menu']").ShouldBeEmpty());
        layout.Find("[data-testid='user-menu']");
    }

    [Fact]
    public async Task A_saved_header_reaches_the_open_app_bar()
    {
        Configure(null);
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.Find("[data-testid='rtl-toggle']"));

        Configure("""[{"id":"rtl","placement":"Hidden"}]""");
        await layout.InvokeAsync(Services.GetRequiredService<ThemeService>().LoadAsync);

        layout.WaitForAssertion(() => layout.FindAll("[data-testid='rtl-toggle']").ShouldBeEmpty());
    }

    [Fact]
    public void A_second_bar_shows_the_items_placed_there_at_its_own_edge_with_its_own_overflow_menu()
    {
        Configure("""[{"id":"about","placement":"SecondBar"},{"id":"rtl","placement":"SecondOverflow"},{"id":"user","placement":"SecondBar"}]""", "Top", "Right");

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar-2']").GetAttribute("data-position").ShouldBe("Right"));
        var second = layout.Find("[data-testid='app-bar-2']");
        second.QuerySelector("[data-testid='about-button']").ShouldNotBeNull();
        second.QuerySelector("[data-testid='user-menu']").ShouldNotBeNull();
        second.QuerySelector("[data-testid='app-bar-more-2']").ShouldNotBeNull();
        var first = layout.Find("[data-testid='app-bar']");
        first.QuerySelector("[data-testid='about-button']").ShouldBeNull();
        first.QuerySelector("[data-testid='user-menu']").ShouldBeNull();
        first.QuerySelector("[data-testid='app-bar-menu']").ShouldNotBeNull();
        layout.Find(".mud-layout").ClassList.ShouldContain("cw-bar-right");
    }

    [Fact]
    public void Without_a_second_bar_its_items_stay_in_the_first_one()
    {
        Configure("""[{"id":"about","placement":"SecondBar"},{"id":"user","placement":"SecondBar"}]""");

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar'] [data-testid='about-button']"));
        layout.Find("[data-testid='app-bar'] [data-testid='user-menu']");
        layout.FindAll("[data-testid='app-bar-2']").ShouldBeEmpty();
    }

    [Fact]
    public void A_bar_at_the_right_edge_alone_leaves_the_menu_button_and_the_name_to_the_navigation()
    {
        Configure(null, "Right");

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar']").TagName.ShouldBe("NAV"));
        layout.Find("[data-testid='app-bar']").GetAttribute("data-position").ShouldBe("Right");
        layout.FindAll(".mud-appbar").ShouldBeEmpty();
        layout.Find("[data-testid='drawer-head'] [data-testid='drawer-toggle']");
        layout.Find(".mud-layout").ClassList.ShouldContain("cw-no-top-bar");
        layout.Find(".mud-drawer").ClassList.ShouldContain("mud-drawer-clipped-never");
    }

    [Fact]
    public async Task A_bar_at_the_right_edge_alone_opens_an_unpinned_navigation_with_its_own_menu_button()
    {
        Configure(null, "Right");
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.Find("[data-testid='drawer-head'] [data-testid='drawer-toggle']"));
        await layout.Find("[data-testid='drawer-toggle']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='nav-pin'] button, button[data-testid='nav-pin']"));

        // the overlay hides when closed: the button in its head went with it
        await layout.Find("[data-testid='nav-pin'] button, button[data-testid='nav-pin']").ClickAsync(new());

        layout.WaitForAssertion(() => layout.FindAll("[data-testid='drawer-head']").ShouldBeEmpty());
        var menu = layout.Find("[data-testid='app-bar'] [data-testid='app-bar-menu']");
        var open = layout.Find(".mud-drawer").ClassList.Contains("mud-drawer--open");
        await menu.ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find(".mud-drawer").ClassList.Contains("mud-drawer--open").ShouldBe(!open));
    }

    [Fact]
    public async Task The_theme_decides_whether_the_app_name_shows_in_the_app_bar_and_at_the_head_of_the_navigation()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AppTitle = "Demo" });
        Configure(null);
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar']").TextContent.ShouldContain("Demo"));

        await layout.InvokeAsync(() => Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { ShowAppNameInAppBar = false }));
        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar']").TextContent.ShouldNotContain("Demo"));

        await layout.InvokeAsync(() => Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { ShowAppNameInAppBar = false, NavBesideAppBar = true }));
        await layout.Find("[data-testid='drawer-toggle']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='drawer-head']").TextContent.ShouldContain("Demo"));

        await layout.InvokeAsync(() => Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { NavBesideAppBar = true, ShowAppNameInNav = false }));
        layout.WaitForAssertion(() => layout.Find("[data-testid='drawer-head']").TextContent.ShouldNotContain("Demo"));
    }

    [Fact]
    public async Task A_theme_puts_the_navigation_beside_the_app_bar_with_the_menu_button_in_its_head()
    {
        Configure(null);
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar-menu']"));
        layout.Find(".mud-drawer").ClassList.ShouldContain("mud-drawer-clipped-always");

        await layout.InvokeAsync(() => Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { NavBesideAppBar = true }));

        layout.WaitForAssertion(() => layout.Find("[data-testid='drawer-head'] [data-testid='drawer-toggle']"));
        layout.FindAll("[data-testid='app-bar-menu']").ShouldBeEmpty();
        layout.Find(".mud-drawer").ClassList.ShouldContain("mud-drawer-clipped-never");
    }

    [Fact]
    public async Task A_saved_position_moves_the_open_app_bar()
    {
        Configure(null);
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar']").GetAttribute("data-position").ShouldBe("Top"));

        Configure(null, "Bottom", "Left");
        await layout.InvokeAsync(Services.GetRequiredService<ThemeService>().LoadAsync);

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar']").GetAttribute("data-position").ShouldBe("Bottom"));
        layout.Find("[data-testid='app-bar-2']").GetAttribute("data-position").ShouldBe("Left");
    }

    [Fact]
    public void Spacers_stay_where_the_administrator_put_them_and_a_hidden_one_is_gone()
    {
        HeaderItem[] available = [Item("a", 1), Item("b", 2)];

        var arranged = HeaderItems.Arrange(available,
            [new("a", HeaderPlacement.Bar), new("spacer", HeaderPlacement.Bar), new("b", HeaderPlacement.Bar), new("spacer-2", HeaderPlacement.Hidden)]);

        arranged.Select(i => i.Item.Id).ShouldBe(["a", "spacer", "b"]);
        HeaderItems.Arrange(available, []).ShouldNotContain(i => HeaderItems.IsSpacer(i.Item.Id), "without a configuration the bar keeps its items at the end");
        HeaderItems.NewSpacerId(["spacer", "spacer-2"]).ShouldBe("spacer-3");
    }

    [Fact]
    public async Task A_spacer_splits_the_bar_and_the_menus_open_towards_its_middle()
    {
        Configure("""[{"id":"user","placement":"Bar"},{"id":"spacer","placement":"Bar"},{"id":"about","placement":"Bar"},{"id":"theme","placement":"Overflow"}]""");

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar'] [data-testid='app-bar-spacer']"));
        var bar = layout.Find(".mud-appbar").InnerHtml;
        bar.IndexOf("user-menu", StringComparison.Ordinal).ShouldBeLessThan(bar.IndexOf("app-bar-spacer", StringComparison.Ordinal));
        bar.IndexOf("app-bar-spacer", StringComparison.Ordinal).ShouldBeLessThan(bar.IndexOf("about-button", StringComparison.Ordinal));
        // the account menu sits at the start now: it opens from its left edge, not off the window
        await layout.Find("[data-testid='user-menu'] button").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find(".mud-popover-open").ClassList.ShouldContain("mud-popover-anchor-bottom-left"));
    }

    [Fact]
    public async Task The_menus_of_a_bar_at_the_bottom_open_upwards()
    {
        Configure("""[{"id":"about","placement":"Overflow"}]""", "Bottom");

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar-more']"));
        await layout.Find("[data-testid='app-bar-more'] button").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find(".mud-popover-open").ClassList.ShouldContain("mud-popover-anchor-top-right"));
        await layout.Find("[data-testid='user-menu'] button").ClickAsync(new());
        layout.WaitForAssertion(() => layout.FindAll(".mud-popover-open").ShouldAllBe(p => p.ClassList.Contains("mud-popover-anchor-top-right")));
    }

    private sealed class Bar : IAppBarContributor
    {
        public IEnumerable<AppBarItem> Items => [new(typeof(LayoutCustomizationTests.LanguageSwitch))];
    }
}
