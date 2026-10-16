using Bunit;
using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.CommandPalette;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CommandPaletteTests : ClientTestBase
{
    private const string ProductsView = "Catalog.Products.View";
    private readonly IAssistantApi _assistant = Substitute.For<IAssistantApi>();
    private readonly FakeODataClient _odata = new();

    public CommandPaletteTests()
    {
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Products", "/catalog/products", Icons.Material.Outlined.Inventory, ProductsView, Group: "Catalog"),
            new CoworkeeNavItem("Users", "/admin/users", Icons.Material.Outlined.Person, "Identity.Users.View", Group: "Administration")));
        Services.AddOptions<NavigationMenuOptions>();
        Services.AddSingleton(_assistant);
        Services.AddSingleton<IODataClient>(_odata);
        Services.AddCommandPaletteProvider<PagesPaletteProvider>();
        Services.AddCommandPaletteProvider<ActionsPaletteProvider>();
        Services.AddCommandPaletteProvider<AssistantPaletteProvider>();
        Services.AddCommandPaletteCommand(new CommandPaletteCommand("New product", Icons.Material.Outlined.Add, ProductsView, Href: "/catalog/products?new=1"));
        Services.AddCommandPaletteEntitySet(new CommandPaletteEntitySet<Gadget>("Gadgets", "/gadgets", g => g.Name, [nameof(Gadget.Name)]));
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([ProductsView]);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(PermissionPolicy.For(ProductsView));
    }

    private IRenderedComponent<CoworkeeCommandPalette> Open()
    {
        var palette = Render<CoworkeeCommandPalette>();
        palette.Find("[data-testid='palette-button']").Click();
        palette.WaitForAssertion(() => palette.FindAll("[data-testid='palette-item']").ShouldNotBeEmpty());
        return palette;
    }

    private static string[] Titles(IRenderedComponent<CoworkeeCommandPalette> palette) =>
        [.. palette.FindAll("[data-testid='palette-item'] .cw-palette-title").Select(t => t.TextContent)];

    [Fact]
    public void Pages_and_commands_show_only_with_their_permission()
    {
        var palette = Open();

        Titles(palette).ShouldContain("Products");
        Titles(palette).ShouldContain("New product");
        Titles(palette).ShouldNotContain("Users");
    }

    [Fact]
    public void Fuzzy_search_ranks_and_highlights_the_typed_letters()
    {
        var palette = Open();

        palette.Find("[data-testid='palette-input']").Input("nwprd");

        palette.WaitForAssertion(() => Titles(palette).ShouldBe(["New product"]));
        palette.FindAll("[data-testid='palette-item'] mark").Select(m => m.TextContent).ShouldBe(["N", "w", "pr", "d"]);
    }

    [Fact]
    public async Task Arrows_and_enter_open_the_chosen_page()
    {
        var palette = Open();
        var input = palette.Find("[data-testid='palette-input']");
        input.Input("prod");
        palette.WaitForAssertion(() => Titles(palette).Take(2).ShouldBe(["Products", "New product"], ignoreOrder: true));
        var second = Titles(palette)[1];

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        palette.Find(".cw-palette-item.active .cw-palette-title").TextContent.ShouldBe(second);
        await palette.Find("[data-testid='palette-input']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        palette.FindAll("[data-testid='palette']").ShouldBeEmpty();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.ShouldEndWith(second == "Products" ? "/catalog/products" : "/catalog/products?new=1");
    }

    [Fact]
    public void Escape_closes_the_palette()
    {
        var palette = Open();

        palette.Find("[data-testid='palette-input']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        palette.FindAll("[data-testid='palette']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Records_of_registered_sets_are_found_and_open_their_page_searched()
    {
        _odata.With("Gadgets", new Gadget(Guid.CreateVersion7(), "Flux capacitor"));
        var palette = Open();

        palette.Find("[data-testid='palette-input']").Input("flux");

        palette.WaitForAssertion(() => palette.FindAll("[data-group='Gadgets']").Count.ShouldBe(1));
        // find and click in one dispatch: the asynchronous search results may re-render in between
        await palette.InvokeAsync(() => palette.Find("[data-group='Gadgets']").Click());
        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("/gadgets?gadgets=");
    }

    [Fact]
    public async Task Unmatched_text_goes_to_the_assistant_when_it_is_available()
    {
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([ProductsView, AiPermissions.Chat]);
        _assistant.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(true));
        var palette = Open();

        palette.Find("[data-testid='palette-input']").Input("how do refunds work");
        palette.WaitForAssertion(() => palette.Find("[data-group='Assistant']").TextContent.ShouldContain("how do refunds work"));

        // find and click in one dispatch: the asynchronous assistant status may re-render the list in between
        await palette.InvokeAsync(() => palette.Find("[data-group='Assistant']").Click());

        var state = Services.GetRequiredService<AssistantState>();
        state.IsOpen.ShouldBeTrue();
        state.Draft.ShouldBe("how do refunds work");
    }

    [Fact]
    public void Without_the_assistant_there_is_no_fallback()
    {
        _assistant.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(true));
        var palette = Open();

        palette.Find("[data-testid='palette-input']").Input("zzqx");

        palette.WaitForAssertion(() => palette.Find("[data-testid='palette-empty']"));
        palette.FindAll("[data-group='Assistant']").ShouldBeEmpty();
    }

    [Fact]
    public void Fuzzy_match_prefers_substrings_and_word_starts()
    {
        FuzzyMatch.Match("prod", "New product")!.Score.ShouldBeGreaterThan(FuzzyMatch.Match("prod", "Approved")?.Score ?? int.MinValue);
        FuzzyMatch.Match("np", "New product")!.Positions.ShouldBe([0, 4]);
        FuzzyMatch.Match("xyz", "New product").ShouldBeNull();
    }

    public sealed record Gadget(Guid Id, string Name);
}
