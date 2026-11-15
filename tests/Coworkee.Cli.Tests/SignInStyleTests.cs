using System.Text.Json.Nodes;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class SignInStyleTests
{
    private static AppSpec Spec(BrandingSpec branding) => new() { Name = "Shop", Branding = branding };

    [Fact]
    public void Every_layout_names_its_backgrounds_and_offers_only_its_options()
    {
        SignInLayoutOptions.Layouts.ShouldBe(["Card", "Split", "Slide", "Spotlight", "Poster", "Stack", "Minimal"]);
        OptionCatalog.Find("signInLayout")!.Choices!.Select(c => c.Value).ShouldBe(SignInLayoutOptions.Layouts);

        SignInLayoutOptions.For("Card").ShouldBe(new("Page background", null, MediaSide: false, Surface: true, Animation: true, Dim: true, Tilt: false, LogoSize: false));
        SignInLayoutOptions.For("Split").ShouldBe(new("Form side", "Media side", MediaSide: true, Surface: false, Animation: false, Dim: false, Tilt: false, LogoSize: false));
        SignInLayoutOptions.For("Poster").PanelLabel.ShouldBe("Poster");
        SignInLayoutOptions.For("Stack").ShouldBe(new("Page background", "Cards behind", MediaSide: false, Surface: false, Animation: true, Dim: true, Tilt: true, LogoSize: false));
        SignInLayoutOptions.For("Minimal").LogoSize.ShouldBeTrue();
        SignInLayoutOptions.For("Mine").ShouldBe(new("Page background", "Panel", MediaSide: true, Surface: true, Animation: true, Dim: true, Tilt: false, LogoSize: false));

        SignInLayoutOptions.With(o => o.Surface).ShouldBe(["Card", "Spotlight"]);
        OptionCatalog.Find("signInSurface")!.DependsOn!.Values.ShouldBe(["Card", "Spotlight"]);
        OptionCatalog.Find("signInTilt")!.DependsOn!.Values.ShouldBe(["Stack"]);
    }

    [Theory]
    [InlineData("Glass", "signInSurface", "Glass")]
    [InlineData("aurora", "signInAnimation", "Aurora")]
    public void The_old_layouts_Glass_and_Aurora_become_Card_with_that_look(string layout, string option, string value)
    {
        var values = SpecOptions.Resolve(Spec(new BrandingSpec { SignInLayout = layout }));

        values["signInLayout"].ShouldBe("Card");
        values[option].ShouldBe(value);
        SpecValidator.Validate(Spec(new BrandingSpec { SignInLayout = layout })).ShouldBeEmpty();
        NewCommand.TemplateArgs(new NewSettings(Spec(new BrandingSpec { SignInLayout = layout }), ".", "shop", null)).ShouldContain("Card");
        SignInStyle.DesignSettings(values)!.ToJsonString().ShouldBe(option == "signInSurface" ? """{"Surface":"Glass"}""" : """{"Animation":"Aurora"}""");
    }

    [Fact]
    public void Fills_reach_the_design_settings_in_the_spelling_of_the_runtime()
    {
        var spec = Spec(new BrandingSpec
        {
            SignInLayout = "Slide",
            SignInPageFill = new SignInFillSpec { Gradient = new SignInGradientSpec { Kind = GradientKind.Radial, Angle = 0, Stops = [new() { Color = "#0a7d5a", Position = 0 }, new() { Color = "rgba(0, 0, 0, .5)", Position = 100 }], Animated = true } },
            SignInPanelFill = new SignInFillSpec { Color = "secondary" },
            SignInBackgroundDim = 40,
        });

        SignInStyle.DesignSettings(SpecOptions.Resolve(spec))!.ToJsonString().ShouldBe(
            """{"BackgroundDim":40,"PageFill":{"Gradient":{"Kind":"Radial","Angle":0,"Stops":[{"Color":"#0a7d5a","Position":0},{"Color":"rgba(0, 0, 0, .5)","Position":100}],"Animated":true}},"PanelFill":{"Color":"secondary"}}""");
        SpecValidator.Validate(spec).ShouldBeEmpty();

        // the spec keeps a fill as an object, an option value as its JSON
        var json = SpecJson.Write(spec);
        json.ShouldContain("\"kind\": \"radial\"");
        SpecJson.Read(json).Branding!.SignInPageFill!.Gradient!.Stops[1].Color.ShouldBe("rgba(0, 0, 0, .5)");
        SpecOptions.Resolve(new AppSpec { Name = "Shop", Options = { ["signInPanelFill"] = JsonNode.Parse("""{ "color": "#123" }""") } })["signInPanelFill"].ShouldBe("""{"color":"#123"}""");
    }

    [Fact]
    public void Defaults_and_options_the_layout_does_not_offer_stay_out_of_the_design_settings()
    {
        SignInStyle.DesignSettings(SpecOptions.Resolve(Spec(new BrandingSpec()))).ShouldBeNull();
        SignInStyle.DesignSettings(SpecOptions.Resolve(Spec(new BrandingSpec { SignInSurface = "Solid", SignInAnimation = "None", SignInLogoSize = "Medium" }))).ShouldBeNull();

        var split = Spec(new BrandingSpec { SignInLayout = "Split", SignInSurface = "Glass", SignInTilt = 4, SignInBackgroundDim = 20, SignInLogoSize = "Large" });
        SignInStyle.DesignSettings(SpecOptions.Resolve(split)).ShouldBeNull();
        SpecValidator.Warnings(split).Select(w => w.Split(':')[0]).ShouldBe(["signInBackgroundDim", "signInSurface", "signInTilt", "signInLogoSize"]);
    }

    [Theory]
    [InlineData("#12345", "is no color")]
    [InlineData("red", "is no color")]
    [InlineData("url(https://evil.test/x.png)", "is no color")]
    [InlineData("rgb(1,2,3);background:url(x)", "is no color")]
    [InlineData("""{"gradient":{"stops":[{"color":"#fff","position":0}]}}""", "needs 2 to 8 stops")]
    [InlineData("""{"gradient":{"angle":400,"stops":[{"color":"#fff","position":0},{"color":"#000","position":100}]}}""", "angle outside")]
    [InlineData("""{"gradient":{"stops":[{"color":"#fff","position":0},{"color":"expression(x)","position":100}]}}""", "no color")]
    [InlineData("""{"gradient":{"stops":[{"color":"#fff","position":0},{"color":"#000","position":101}]}}""", "outside 0 to 100")]
    [InlineData("""{"color":"#fff","gradient":{"stops":[]}}""", "neither")]
    [InlineData("{oops", "neither")]
    public void A_fill_takes_only_colors_the_sign_in_pages_accept(string fill, string problem)
    {
        var errors = SpecValidator.Validate(new AppSpec { Name = "Shop", Options = { ["signInPageFill"] = fill } });

        errors.ShouldHaveSingleItem().ShouldStartWith("signInPageFill:");
        errors[0].ShouldContain(problem);
    }

    [Fact]
    public void Colors_of_every_accepted_form_pass_and_numbers_stay_in_their_range()
    {
        foreach (var color in new[] { "#abc", "#a1b2c3", "#a1b2c3d4", "rgb(1, 2, 3)", "rgba(1,2,3,.5)", "hsl(120, 50%, 40%)", "hsla(120 50% 40% / 0.3)", "primary", "tertiary", "surface" })
        {
            SignInStyle.Problem(color).ShouldBeNull(color);
        }

        SpecValidator.Validate(Spec(new BrandingSpec { SignInLayout = "Stack", SignInTilt = 13, SignInBackgroundDim = 95 })).ShouldBe(
        [
            "signInBackgroundDim: '95' is not a whole number from 0 to 90.",
            "signInTilt: '13' is not a whole number from 0 to 12.",
        ]);
    }

    [Fact]
    public void A_local_sign_in_background_goes_to_the_media_of_the_web_client()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse(["new", "Shop", "--sign-in-background", "https://cdn.test/page.jpg", "--sign-in-page-fill", "#0a7d5a", "--sign-in-layout", "Spotlight", "--sign-in-surface", "Glass"]));

        var args = NewCommand.TemplateArgs(settings);
        args[Array.IndexOf(args, "--signInBackground") + 1].ShouldBe("https://cdn.test/page.jpg");
        args.ShouldNotContain("--signInSurface"); // written into the appsettings, not a template switch
        Assets.TemplateValue("signInBackground", "brand/page.jpg").ShouldBe("/media/page.jpg");
        SignInStyle.DesignSettings(SpecOptions.Resolve(settings.Spec))!.ToJsonString().ShouldBe("""{"PageFill":{"Color":"#0a7d5a"},"Surface":"Glass"}""");
    }

    [Fact]
    public void The_design_merges_into_existing_appsettings()
    {
        using var folder = new TempFolder();
        var file = folder.File("appsettings.json");
        File.WriteAllText(file, """
            {
              // comments are fine
              "Coworkee": { "Auth": { "Design": { "Layout": "Card", "Tilt": 2 } }, "Settings": { "A": "1" } }
            }
            """);

        SignInDesignWriter.Merge(file, new JsonObject { ["Tilt"] = 8, ["LogoSize"] = "Large" });

        var root = JsonNode.Parse(File.ReadAllText(file))!;
        root["Coworkee"]!["Auth"]!["Design"]!.ToJsonString().ShouldBe("""{"Layout":"Card","Tilt":8,"LogoSize":"Large"}""");
        root["Coworkee"]!["Settings"]!["A"]!.GetValue<string>().ShouldBe("1");

        var fresh = folder.File("new.json");
        SignInDesignWriter.Merge(fresh, new JsonObject { ["Surface"] = "Glass" });
        JsonNode.Parse(File.ReadAllText(fresh))!.ToJsonString().ShouldBe("""{"Coworkee":{"Auth":{"Design":{"Surface":"Glass"}}}}""");
    }
}
