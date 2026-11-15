using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The named sign-in designs on the page Sign-in and the sign-in design of a theme in the theme editor.</summary>
public sealed class SignInDesignListTests : ClientTestBase
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });

    private int _changes;

    public SignInDesignListTests()
    {
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([Theme("Ocean", null), Theme("Night", "night"), Theme("Old", "gone")]);
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([]);
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).Returns(new AppConfigurationValuesDto(SignInDesign.Section,
            JsonSerializer.SerializeToElement(Design(), JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new SignInDesign(), JsonSerializerOptions.Web), []));
    }

    private static SignInDesign Design() => new()
    {
        Layout = SignInLayouts.Card,
        Texts = new SignInTexts { Title = "Hi" },
        Designs = [new NamedSignInDesign { Key = "night", Name = "Night", Layout = SignInLayouts.Stack }],
    };

    private static ThemeDto Theme(string name, string? design) => new(Guid.NewGuid(), name, true, false, Empty, Empty, null, null, null, null, 1,
        Options: design is null ? null : JsonSerializer.SerializeToElement(new Dictionary<string, string> { [SignInDesign.ThemeOption] = design }));

    [Fact]
    public void Lists_the_default_and_the_named_designs_with_the_themes_that_show_them()
    {
        var list = RenderList(Design());

        list.WaitForAssertion(() => Tiles(list)[0].TextContent.ShouldContain("Used by Ocean, Old"));
        Tiles(list).Select(t => t.GetAttribute("data-key")).ShouldBe(["", "night"]);
        Tiles(list)[0].TextContent.ShouldContain("Default design");
        Tiles(list)[0].TextContent.ShouldContain("Card");
        Tiles(list)[0].GetAttribute("aria-selected").ShouldBe("true");
        Tiles(list)[1].TextContent.ShouldContain("Night");
        Tiles(list)[1].TextContent.ShouldContain("Stack");
        Tiles(list)[1].TextContent.ShouldContain("Used by Night");
        list.Find("[data-testid='sign-in-design'] h6").TextContent.ShouldBe("Default design");
        list.FindAll("[data-testid='sign-in-design-delete']").ShouldBeEmpty();
        // the default design keeps the loading screen
        list.FindComponents<MudSelect<LoadingScreenBackground>>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Adding_a_design_selects_it_and_the_card_edits_its_look_only()
    {
        var design = Design();
        var list = RenderList(design);

        await list.InvokeAsync(() => list.Find("[data-testid='sign-in-design-add']").ClickAsync(new()));
        await list.InvokeAsync(() => list.Find("[data-testid='sign-in-design'] [data-layout='Split']").ClickAsync(new()));

        var added = design.Designs.Single(d => d.Key != "night");
        added.Key.ShouldStartWith("design-");
        added.Name.ShouldBe("New design");
        added.Layout.ShouldBe(SignInLayouts.Split);
        design.Layout.ShouldBe(SignInLayouts.Card);
        Tiles(list)[2].GetAttribute("aria-selected").ShouldBe("true");
        list.Find("[data-testid='sign-in-design'] h6").TextContent.ShouldBe("New design");
        list.FindComponents<MudSelect<LoadingScreenBackground>>().ShouldBeEmpty();
        _changes.ShouldBe(2);

        await list.InvokeAsync(() => list.Find("[data-testid='sign-in-design-add']").ClickAsync(new()));
        design.Designs.Select(d => d.Name).ShouldBe(["Night", "New design", "New design 2"]);
    }

    [Fact]
    public async Task Duplicating_copies_the_look_of_the_selected_design_under_a_new_name_and_key()
    {
        var design = Design();
        design.Designs[0].BackgroundUrl = "/media/sky.jpg";
        var list = RenderList(design);

        await list.InvokeAsync(() => Tiles(list)[1].ClickAsync(new()));
        await list.InvokeAsync(() => list.Find("[data-testid='sign-in-design-duplicate']").ClickAsync(new()));
        await list.InvokeAsync(() => Tiles(list)[0].ClickAsync(new()));
        await list.InvokeAsync(() => list.Find("[data-testid='sign-in-design-duplicate']").ClickAsync(new()));

        design.Designs.Select(d => (d.Name, d.Layout, d.BackgroundUrl)).ShouldBe(
            [("Night", SignInLayouts.Stack, "/media/sky.jpg"), ("Night (copy)", SignInLayouts.Stack, "/media/sky.jpg"), ("Default design (copy)", SignInLayouts.Card, null)]);
        design.Designs.Select(d => d.Key).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Renaming_changes_the_name_and_keeps_the_key_themes_store()
    {
        var design = Design();
        var list = RenderList(design);

        await list.InvokeAsync(() => Tiles(list)[1].ClickAsync(new()));
        list.Find("[data-testid='sign-in-design-name'] input, input[data-testid='sign-in-design-name']").Change("Midnight");

        design.Designs[0].ShouldSatisfyAllConditions(d => d.Name.ShouldBe("Midnight"), d => d.Key.ShouldBe("night"));
        list.WaitForAssertion(() => Tiles(list)[1].TextContent.ShouldContain("Midnight"));
    }

    [Fact]
    public async Task Deleting_warns_with_the_themes_that_use_the_design_and_selects_the_default_again()
    {
        var dialogs = Render<MudDialogProvider>();
        var design = Design();
        var list = RenderList(design);
        list.WaitForAssertion(() => Tiles(list)[1].TextContent.ShouldContain("Used by Night"));
        await list.InvokeAsync(() => Tiles(list)[1].ClickAsync(new()));

        var deleting = list.InvokeAsync(() => list.Find("[data-testid='sign-in-design-delete']").ClickAsync(new()));
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Night use this design and show the default design once it is deleted (after saving)."));
        await dialogs.FindAll("button").Last(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        design.Designs.ShouldBeEmpty();
        list.WaitForAssertion(() => Tiles(list).ShouldHaveSingleItem().GetAttribute("aria-selected").ShouldBe("true"));
        Tiles(list)[0].TextContent.ShouldContain("Used by Night, Ocean, Old");
    }

    [Fact]
    public async Task The_preview_of_a_named_design_shows_its_look_with_the_texts_of_the_default_design_and_without_the_list()
    {
        var dialogs = Render<MudDialogProvider>();
        var list = RenderList(Design());

        await list.InvokeAsync(() => Tiles(list)[1].ClickAsync(new()));
        await list.InvokeAsync(() => list.Find("[data-testid='sign-in-preview']").ClickAsync(new()));

        var src = dialogs.WaitForElement("[data-testid='sign-in-preview-frame']").GetAttribute("src")!;
        var shown = JsonSerializer.Deserialize<SignInDesign>(Uri.UnescapeDataString(src[(src.IndexOf('=') + 1)..]), JsonSerializerOptions.Web)!;
        shown.Layout.ShouldBe(SignInLayouts.Stack);
        shown.Texts.Title.ShouldBe("Hi");
        shown.Designs.ShouldBeEmpty();
    }

    [Fact]
    public void The_card_offers_the_seven_layouts_with_their_own_thumbnails()
    {
        var list = RenderList(Design());

        list.FindAll("[data-testid='sign-in-design'] [data-testid='sign-in-layout']").Select(t => t.GetAttribute("data-layout")).ShouldBe(SignInLayouts.All);
        var thumbs = SignInLayouts.All.Select(l => list.Find($"[data-testid='sign-in-design'] [data-thumb='{l}']").InnerHtml).ToList();
        thumbs.Distinct().Count().ShouldBe(SignInLayouts.All.Count);
        list.Find("[data-testid='sign-in-design'] [data-thumb='Stack']").InnerHtml.ShouldContain("rotate(");
    }

    [Fact]
    public async Task A_new_design_starts_as_a_glass_card()
    {
        var design = Design();
        var list = RenderList(design);

        await list.InvokeAsync(() => list.Find("[data-testid='sign-in-design-add']").ClickAsync(new()));

        design.Designs[^1].ShouldSatisfyAllConditions(d => d.Layout.ShouldBe(SignInLayouts.Card), d => d.Surface.ShouldBe(SignInSurface.Glass));
    }

    [Fact]
    public void The_tiles_show_the_fills_of_each_design()
    {
        var design = Design();
        design.Designs[0].PanelFill = new SignInFill { Color = "#ff5f6d" };

        var list = RenderList(design);

        Tiles(list)[1].InnerHtml.ShouldContain("#ff5f6d");
    }

    [Fact]
    public async Task The_theme_editor_chooses_the_default_design_or_a_named_one()
    {
        var popovers = Render<MudPopoverProvider>();
        string? value = "night";
        var select = Render<SignInDesignSelect>(p => p.Add(s => s.Value, value).Add(s => s.ValueChanged, v => value = v));

        select.WaitForElement("[data-testid='theme-sign-in-design']").MouseDown();
        var items = popovers.WaitForElements(".mud-list-item");
        items.Select(i => i.TextContent.Trim()).ShouldBe(["Default design", "Night"]);
        await items[0].ClickAsync(new());

        value.ShouldBeNull();
    }

    [Fact]
    public void A_theme_naming_a_design_that_is_gone_or_not_readable_keeps_it_with_a_hint()
    {
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).ThrowsAsync(new ApiException(403, null, null));
        var popovers = Render<MudPopoverProvider>();
        var select = Render<SignInDesignSelect>(p => p.Add(s => s.Value, "night"));

        select.WaitForElement("[data-testid='theme-sign-in-design']").MouseDown();

        popovers.WaitForElements(".mud-list-item").Select(i => i.TextContent.Trim()).ShouldBe(["Default design", "night (not found, default design)"]);
    }

    [Fact]
    public void Without_the_sign_in_design_the_theme_editor_shows_no_choice()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowSignInDesign = false });

        Render<SignInDesignSelect>().FindAll("[data-testid='theme-sign-in-design']").ShouldBeEmpty();
        Api.DidNotReceive().GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_theme_keeps_its_sign_in_design_in_its_options()
    {
        var request = ThemeMapper.ToRequest("Night", new CoworkeeTheme { SignInDesign = "night" });

        SignInDesign.KeyOf(request.Options).ShouldBe("night");
        ThemeMapper.ToTheme(new ThemeDto(Guid.NewGuid(), "Night", false, false, request.PaletteLight, request.PaletteDark, null, null, null, null, 1, Options: request.Options))
            .SignInDesign.ShouldBe("night");
        SignInDesign.KeyOf(ThemeMapper.ToRequest("Plain", new CoworkeeTheme()).Options).ShouldBeNull();
    }

    private IRenderedComponent<SignInDesignList> RenderList(SignInDesign design)
    {
        Render<MudPopoverProvider>();
        return Render<SignInDesignList>(p => p.Add(l => l.Design, design).Add(l => l.Changed, () => _changes++));
    }

    private static List<AngleSharp.Dom.IElement> Tiles(IRenderedComponent<SignInDesignList> list) => [.. list.FindAll("[data-testid='sign-in-design-tile']")];
}
