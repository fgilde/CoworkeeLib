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
        Designs = [new NamedSignInDesign { Key = "night", Name = "Night", Layout = SignInLayouts.Aurora, BackgroundUrl = "/media/sky.jpg", BackgroundDim = 30 }],
    };

    [Fact]
    public void A_theme_naming_a_design_gets_its_look_with_the_texts_and_app_name_of_the_default_design()
    {
        var shown = Design().For("NIGHT");

        shown.Layout.ShouldBe(SignInLayouts.Aurora);
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
        design.For("glass").Layout.ShouldBe(SignInLayouts.Glass);
    }

    [Fact]
    public void Only_split_and_slide_have_a_media_panel()
    {
        SignInLayouts.All.Where(SignInLayouts.HasPanel).ShouldBe([SignInLayouts.Split, SignInLayouts.Slide]);
        SignInLayouts.HasPanel(null).ShouldBeTrue(); // the default, slide
        SignInLayouts.All.ShouldContain(SignInLayouts.Glass);
        SignInLayouts.All.ShouldContain(SignInLayouts.Aurora);
    }
}
