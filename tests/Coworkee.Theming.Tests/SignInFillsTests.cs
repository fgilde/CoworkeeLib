using Coworkee.Contracts.Theming;

namespace Coworkee.Theming.Tests;

/// <summary>Fills of the sign-in designs end up in style attributes and style sheets: only checked parts may come through.</summary>
public sealed class SignInFillsTests
{
    private static string Theme(string color) => $"var(--{color})";

    [Theory]
    [InlineData("#abc")]
    [InlineData("#A1B2C3")]
    [InlineData("#a1b2c3d4")]
    [InlineData("rgb(1, 2, 3)")]
    [InlineData("rgba(10,20,30,0.5)")]
    [InlineData("rgba(10 20 30 / .5)")]
    [InlineData("hsl(120, 40%, 60%)")]
    [InlineData("hsla(120deg 40% 60% / 50%)")]
    [InlineData("primary")]
    [InlineData(" Surface ")]
    public void Color_values_and_theme_colors_pass(string color) => SignInFills.IsColor(color).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#abcd1")]
    [InlineData("#12345g")]
    [InlineData("var(--x)")]
    [InlineData("url(https://evil.test/x.png)")]
    [InlineData("rgb(1,2,3);background:url(x)")]
    [InlineData("rgb(1,2,3)}*{x:y")]
    [InlineData("expression(alert(1))")]
    [InlineData("rgb(calc(1),2,3)")]
    [InlineData("primary;x")]
    [InlineData("\"><script>")]
    [InlineData("rgb(1111,2,3)")]
    public void Anything_else_is_dropped(string? color)
    {
        SignInFills.IsColor(color).ShouldBeFalse();
        SignInFills.Checked(new SignInFill { Color = color }).ShouldBeNull();
        SignInFills.Css(new SignInFill { Color = color }, Theme).ShouldBeNull();
    }

    [Fact]
    public void A_color_too_long_is_dropped() => SignInFills.IsColor("rgb(" + new string(' ', 70) + "1,2,3)").ShouldBeFalse();

    [Fact]
    public void A_gradient_is_rebuilt_from_its_checked_stops_in_order_with_clamped_numbers()
    {
        var fill = new SignInFill
        {
            Color = "#ffffff",
            Gradient = new SignInGradient
            {
                Kind = SignInGradientKind.Linear, Angle = 450,
                Stops = [new() { Color = "Tertiary", Position = 140 }, new() { Color = "red;}", Position = 50 }, new() { Color = "#000", Position = -5 }],
            },
        };

        SignInFills.Css(fill, Theme).ShouldBe("linear-gradient(90deg, #000 0%, var(--tertiary) 100%)");
        var clean = SignInFills.Checked(fill)!;
        clean.Color.ShouldBeNull();
        clean.Gradient!.Stops.Select(s => (s.Color, s.Position)).ShouldBe([("#000", 0), ("tertiary", 100)]);
    }

    [Theory]
    [InlineData(SignInGradientKind.Radial, "radial-gradient(circle at 50% 50%, #111 0%, #222 100%)")]
    [InlineData(SignInGradientKind.Conic, "conic-gradient(from 45deg at 50% 50%, #111 0%, #222 100%)")]
    [InlineData((SignInGradientKind)9, "linear-gradient(45deg, #111 0%, #222 100%)")]
    public void Every_kind_has_its_css(SignInGradientKind kind, string css) => SignInFills.Css(new SignInFill
    {
        Gradient = new SignInGradient { Kind = kind, Angle = 45, Stops = [new() { Color = "#111", Position = 0 }, new() { Color = "#222", Position = 100 }] },
    }, Theme).ShouldBe(css);

    [Fact]
    public void At_most_eight_stops_count()
    {
        var gradient = new SignInGradient { Stops = [.. Enumerable.Range(0, 12).Select(i => new SignInGradientStop { Color = "#000", Position = i })] };

        SignInFills.Checked(new SignInFill { Gradient = gradient })!.Gradient!.Stops.Count.ShouldBe(SignInFills.MaxStops);
    }

    [Fact]
    public void A_gradient_with_one_valid_stop_is_that_color_and_without_any_falls_back_to_the_color()
    {
        SignInFills.Checked(new SignInFill { Gradient = new SignInGradient { Stops = [new() { Color = "#123", Position = 10 }, new() { Color = "nope", Position = 90 }] } })!
            .ShouldSatisfyAllConditions(f => f.Color.ShouldBe("#123"), f => f.Gradient.ShouldBeNull());
        SignInFills.Checked(new SignInFill { Color = "secondary", Gradient = new SignInGradient { Stops = [] } })!.Color.ShouldBe("secondary");
        SignInFills.Checked(null).ShouldBeNull();
        SignInFills.Checked(new SignInFill()).ShouldBeNull();
    }

    [Fact]
    public void Animation_and_colors_come_from_the_checked_fill()
    {
        var fill = new SignInFill { Gradient = new SignInGradient { Animated = true, Stops = [new() { Color = "primary", Position = 0 }, new() { Color = "#fff", Position = 100 }] } };

        SignInFills.IsAnimated(fill).ShouldBeTrue();
        SignInFills.IsAnimated(new SignInFill { Color = "#fff" }).ShouldBeFalse();
        SignInFills.ColorsOf(fill, Theme).ShouldBe(["var(--primary)", "#fff"]);
        SignInFills.ColorsOf(null, Theme).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("#000", true)]
    [InlineData("#0f2027", true)]
    [InlineData("rgb(250, 250, 250)", false)]
    [InlineData("#ffc371", false)]
    [InlineData("primary", null)]
    [InlineData("hsl(0, 0%, 0%)", null)]
    public void Text_on_a_fill_follows_its_brightness(string color, bool? dark) => SignInFills.IsDark(new SignInFill { Color = color }).ShouldBe(dark);

    [Fact]
    public void Each_layout_names_its_options()
    {
        SignInLayoutOptions.For(SignInLayouts.Card).ShouldBe(new SignInLayoutOptions("Page background", null, null, true, true, true, false, false));
        SignInLayoutOptions.For(SignInLayouts.Split).ShouldBe(new SignInLayoutOptions("Form side", "Media side", "Image side", false, false, false, false, false));
        SignInLayoutOptions.For(SignInLayouts.Slide).ShouldBe(new SignInLayoutOptions("Page background", "Panel", "Panel side", false, false, true, false, false));
        SignInLayoutOptions.For(SignInLayouts.Spotlight).ShouldBe(new SignInLayoutOptions("Page background", null, "Panel side", true, true, true, false, false));
        SignInLayoutOptions.For(SignInLayouts.Poster).ShouldBe(new SignInLayoutOptions("Form side", "Poster", "Poster side", false, false, false, false, false));
        SignInLayoutOptions.For(SignInLayouts.Stack).ShouldBe(new SignInLayoutOptions("Page background", "Cards behind", null, false, true, true, true, false));
        SignInLayoutOptions.For(SignInLayouts.Minimal).ShouldBe(new SignInLayoutOptions("Page background", null, null, false, true, true, false, true));
        SignInLayoutOptions.For("MyOwn").ShouldBe(new SignInLayoutOptions("Page background", "Panel", "Image side", true, true, true, false, false));
    }

    [Theory]
    [InlineData(null, SignInLayouts.Slide)]
    [InlineData("", SignInLayouts.Slide)]
    [InlineData("glass", SignInLayouts.Card)]
    [InlineData("AURORA", SignInLayouts.Card)]
    [InlineData("stack", SignInLayouts.Stack)]
    public void The_default_and_the_former_layouts_get_the_options_of_what_they_show(string? layout, string shown) =>
        SignInLayoutOptions.For(layout).ShouldBe(SignInLayoutOptions.For(shown));
}
