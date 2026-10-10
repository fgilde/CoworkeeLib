using System.Text.Json;

namespace Coworkee.Theming.Tests;

public sealed class ThemeSeedTests
{
    [Fact]
    public void The_options_choose_the_default_and_unknown_names_keep_ocean()
    {
        ThemeSeeds.Create(new ThemingOptions { DefaultTheme = "Forest" }).Single(t => t.IsDefault).Name.ShouldBe("Forest");
        ThemeSeeds.Create(new ThemingOptions { DefaultTheme = "Nope" }).Single(t => t.IsDefault).Name.ShouldBe("Ocean");
    }

    [Fact]
    public void An_app_theme_takes_its_base_theme_with_other_colors_and_readable_contrast()
    {
        var options = new ThemingOptions { DefaultTheme = "Shop" };
        options.Themes.Add(new AppTheme("Shop", "Forest", new Dictionary<string, string> { ["Primary"] = "#ffeb3b", ["Secondary"] = "#0d47a1" }));

        var themes = ThemeSeeds.Create(options);
        var shop = themes.Single(t => t.Name == "Shop");
        var forest = themes.Single(t => t.Name == "Forest");

        shop.IsDefault.ShouldBeTrue();
        shop.IsPublished.ShouldBeTrue();
        shop.LayoutProperties.ShouldBe(forest.LayoutProperties);
        var light = JsonSerializer.Deserialize<Dictionary<string, string>>(shop.PaletteLight)!;
        light["Primary"].ShouldBe("#ffeb3b");
        light["PrimaryContrastText"].ShouldBe("#000000");
        light["SecondaryContrastText"].ShouldBe("#ffffff");
        light["Background"].ShouldBe(JsonSerializer.Deserialize<Dictionary<string, string>>(forest.PaletteLight)!["Background"]);
        JsonSerializer.Deserialize<Dictionary<string, string>>(shop.PaletteDark)!["Primary"].ShouldBe("#ffeb3b");
    }

    [Theory]
    [InlineData("#ffffff", "#000000")]
    [InlineData("#000000", "#ffffff")]
    [InlineData("#1565c0", "#ffffff")]
    [InlineData("red", null)]
    public void Contrast_text_follows_the_luminance(string color, string? expected) => ThemeSeeds.ContrastText(color).ShouldBe(expected);
}
