using System.Text.Json;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Theming.Tests;

public sealed class SignInDesignTests
{
    private static SignInDesign Design() => new()
    {
        Layout = SignInLayouts.Split,
        MediaUrl = "/media/hero.jpg",
        ShowAppName = false,
        LoadingScreen = LoadingScreenBackground.Plain,
        Texts = new SignInTexts { Title = "Welcome" },
        Designs = [new NamedSignInDesign { Key = "night", Name = "Night", Layout = SignInLayouts.Stack, BackgroundUrl = "/media/sky.jpg", BackgroundDim = 30, Tilt = 9,
            PanelFill = new SignInFill { Color = "primary" } }],
    };

    [Fact]
    public void A_theme_naming_a_design_gets_its_look_with_the_texts_and_app_name_of_the_default_design()
    {
        var shown = Design().For("NIGHT");

        shown.Layout.ShouldBe(SignInLayouts.Stack);
        shown.Tilt.ShouldBe(9);
        shown.PanelFill!.Color.ShouldBe("primary");
        shown.PanelFill.ShouldNotBeSameAs(Design().Designs[0].PanelFill);
        shown.MediaUrl.ShouldBeNull();
        shown.BackgroundUrl.ShouldBe("/media/sky.jpg");
        shown.BackgroundDim.ShouldBe(30);
        shown.ShowAppName.ShouldBeFalse();
        shown.LoadingScreen.ShouldBe(LoadingScreenBackground.Plain);
        shown.Texts.Title.ShouldBe("Welcome");
        shown.Designs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gone")]
    public void Without_a_design_or_with_one_that_is_gone_the_default_design_shows(string? key)
    {
        var design = Design();

        design.For(key).ShouldBeSameAs(design);
    }

    [Fact]
    public void The_key_comes_from_the_theme_options()
    {
        SignInDesign.KeyOf(JsonSerializer.SerializeToElement(new Dictionary<string, object> { [SignInDesign.ThemeOption] = "night", ["Dense"] = true })).ShouldBe("night");
        SignInDesign.KeyOf(JsonSerializer.SerializeToElement(new Dictionary<string, object> { [SignInDesign.ThemeOption] = "" })).ShouldBeNull();
        SignInDesign.KeyOf(JsonSerializer.SerializeToElement(new Dictionary<string, object> { [SignInDesign.ThemeOption] = 3 })).ShouldBeNull();
        SignInDesign.KeyOf(JsonSerializer.SerializeToElement(new { SignIn = new { Layout = "Split" } })).ShouldBeNull(); // the former per theme design
        SignInDesign.KeyOf(null).ShouldBeNull();
    }

    [Fact]
    public void A_stored_design_from_before_the_named_designs_reads_as_before()
    {
        var design = JsonSerializer.Deserialize<SignInDesign>("""{"layout":"Split","mediaUrl":"/media/welcome.jpg","mediaSide":"Right","texts":{"title":"Hi"}}""", JsonSerializerOptions.Web)!;

        design.Designs.ShouldBeEmpty();
        design.For(null).ShouldBeSameAs(design);
        design.Layout.ShouldBe(SignInLayouts.Split);
        design.MediaSide.ShouldBe(SignInMediaSide.Right);
        design.Texts.Title.ShouldBe("Hi");
    }

    [Fact]
    public void The_configuration_binds_the_named_designs()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Design:Layout"] = "Card",
            ["Design:Designs:0:Key"] = "glass",
            ["Design:Designs:0:Name"] = "Frosted",
            ["Design:Designs:0:Layout"] = "Glass",
            ["Design:Designs:0:MediaUrl"] = "/media/sea.mp4",
        }).Build();

        var design = configuration.GetSection("Design").Get<SignInDesign>()!;

        design.Layout.ShouldBe(SignInLayouts.Card);
        design.Find("glass").ShouldNotBeNull().ShouldSatisfyAllConditions(d => d.Name.ShouldBe("Frosted"), d => d.MediaUrl.ShouldBe("/media/sea.mp4"));
        // read as stored; the pages and the editor turn it into the card it became (Normalize)
        design.For("glass").Layout.ShouldBe("Glass");
    }

    [Fact]
    public void Split_slide_and_poster_have_a_media_panel_beside_the_form()
    {
        SignInLayouts.All.Where(SignInLayouts.HasPanel).ShouldBe([SignInLayouts.Split, SignInLayouts.Slide, SignInLayouts.Poster]);
        SignInLayouts.HasPanel(null).ShouldBeTrue(); // the default, slide
    }

    [Fact]
    public void There_are_seven_layouts_and_glass_and_aurora_are_no_longer_among_them()
    {
        SignInLayouts.All.ShouldBe(["Card", "Split", "Slide", "Spotlight", "Poster", "Stack", "Minimal"]);
        SignInLayouts.Default.ShouldBe(SignInLayouts.Slide);
        SignInLayouts.IsFormer("glass").ShouldBeTrue();
        SignInLayouts.IsFormer("Aurora").ShouldBeTrue();
        SignInLayouts.IsFormer("Card").ShouldBeFalse();
        SignInLayouts.IsFormer(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Glass", null, SignInSurface.Glass, null, "/media/sea.mp4")]
    [InlineData("glass", "/media/sky.jpg", SignInSurface.Glass, null, "/media/sky.jpg")]
    [InlineData("Aurora", null, null, SignInAnimation.Aurora, null)]
    public void Stored_glass_and_aurora_designs_become_the_card_with_surface_and_animation(string layout, string? background, SignInSurface? surface, SignInAnimation? animation, string? expectedBackground)
    {
        var design = new SignInDesign { Layout = layout, MediaUrl = "/media/sea.mp4", BackgroundUrl = background, Designs = [new NamedSignInDesign { Key = "n", Layout = layout }] };

        design.Normalize();

        design.Layout.ShouldBe(SignInLayouts.Card);
        design.Surface.ShouldBe(surface);
        design.Animation.ShouldBe(animation);
        design.BackgroundUrl.ShouldBe(expectedBackground);
        design.MediaUrl.ShouldBe("/media/sea.mp4"); // the loading screen may still show it
        design.Designs[0].Layout.ShouldBe(SignInLayouts.Card);
    }

    [Fact]
    public void Normalizing_keeps_other_layouts_and_own_settings_and_can_keep_the_former_name()
    {
        var split = new SignInDesign { Layout = SignInLayouts.Split };
        split.Normalize();
        var solid = new SignInDesign { Layout = "Glass", Surface = SignInSurface.Solid };
        solid.Normalize();
        var kept = new SignInDesign { Layout = "Aurora" };
        kept.Normalize(keepLayout: true);

        split.Layout.ShouldBe(SignInLayouts.Split);
        split.Surface.ShouldBeNull();
        solid.Surface.ShouldBe(SignInSurface.Solid);
        kept.Layout.ShouldBe("Aurora");
        kept.Animation.ShouldBe(SignInAnimation.Aurora);
    }

    [Fact]
    public void Copying_a_look_copies_every_option_and_the_fills_as_copies()
    {
        var from = new NamedSignInDesign
        {
            Layout = SignInLayouts.Minimal, Surface = SignInSurface.Glass, Animation = SignInAnimation.Aurora, Tilt = 4, LogoSize = SignInLogoSize.Large,
            PageFill = new SignInFill { Gradient = new SignInGradient { Kind = SignInGradientKind.Conic, Angle = 40, Animated = true, Stops = [new() { Color = "#000", Position = 0 }, new() { Color = "#fff", Position = 100 }] } },
            PanelFill = new SignInFill { Color = "secondary" },
        };
        var to = new NamedSignInDesign();

        to.CopyLook(from);
        to.PageFill!.Gradient!.Stops[0].Color = "#123";

        to.ShouldSatisfyAllConditions(
            t => t.Layout.ShouldBe(SignInLayouts.Minimal), t => t.Surface.ShouldBe(SignInSurface.Glass), t => t.Animation.ShouldBe(SignInAnimation.Aurora),
            t => t.Tilt.ShouldBe(4), t => t.LogoSize.ShouldBe(SignInLogoSize.Large), t => t.PanelFill!.Color.ShouldBe("secondary"),
            t => t.PageFill!.Gradient!.Kind.ShouldBe(SignInGradientKind.Conic), t => t.PageFill!.Gradient!.Animated.ShouldBeTrue());
        from.PageFill.Gradient.Stops[0].Color.ShouldBe("#000");
    }

    [Fact]
    public void The_new_options_round_trip_through_json_and_the_configuration()
    {
        var json = """{"layout":"Stack","surface":"Glass","animation":"Aurora","tilt":8,"logoSize":"Small","pageFill":{"color":"#101010"},"panelFill":{"gradient":{"kind":"Radial","angle":0,"stops":[{"color":"primary","position":0},{"color":"#fff","position":100}],"animated":true}}}""";
        var design = JsonSerializer.Deserialize<SignInDesign>(json, JsonSerializerOptions.Web)!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Design:Layout"] = "Stack",
            ["Design:Surface"] = "Glass",
            ["Design:PanelFill:Gradient:Kind"] = "Conic",
            ["Design:PanelFill:Gradient:Stops:0:Color"] = "#000",
            ["Design:PanelFill:Gradient:Stops:1:Color"] = "#fff",
            ["Design:PanelFill:Gradient:Stops:1:Position"] = "100",
        }).Build().GetSection("Design").Get<SignInDesign>()!;

        design.Surface.ShouldBe(SignInSurface.Glass);
        design.LogoSize.ShouldBe(SignInLogoSize.Small);
        design.PanelFill!.Gradient!.Kind.ShouldBe(SignInGradientKind.Radial);
        JsonSerializer.Serialize(design, JsonSerializerOptions.Web).ShouldContain("\"surface\":\"Glass\"");
        configuration.Surface.ShouldBe(SignInSurface.Glass);
        configuration.PanelFill!.Gradient!.Kind.ShouldBe(SignInGradientKind.Conic);
        configuration.PanelFill.Gradient.Stops.Select(s => s.Position).ShouldBe([0, 100]);
    }
}
