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

    private void Configure(string? json) =>
        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { [HeaderSettings.Items] = json });

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
    public async Task A_saved_header_reaches_the_open_app_bar()
    {
        Configure(null);
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.Find("[data-testid='rtl-toggle']"));

        Configure("""[{"id":"rtl","placement":"Hidden"}]""");
        await layout.InvokeAsync(Services.GetRequiredService<ThemeService>().LoadAsync);

        layout.WaitForAssertion(() => layout.FindAll("[data-testid='rtl-toggle']").ShouldBeEmpty());
    }

    private sealed class Bar : IAppBarContributor
    {
        public IEnumerable<AppBarItem> Items => [new(typeof(LayoutCustomizationTests.LanguageSwitch))];
    }
}
