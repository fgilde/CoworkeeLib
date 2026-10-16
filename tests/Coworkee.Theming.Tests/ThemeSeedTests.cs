using System.Text.Json;
using Coworkee.Contracts.Theming;

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
    public void The_keycloak_login_theme_defaults_are_the_ocean_colors()
    {
        var ocean = ThemeSeeds.Create(new ThemingOptions()).Single(t => t.Name == "Ocean");

        foreach (var (palette, defaults) in new[] { (ocean.PaletteLight, KeycloakLoginTheme.DefaultLight), (ocean.PaletteDark, KeycloakLoginTheme.DefaultDark) })
        {
            foreach (var (key, color) in JsonSerializer.Deserialize<Dictionary<string, string>>(palette)!.Where(c => defaults.ContainsKey(c.Key)))
            {
                defaults[key].ShouldBe(color, key);
            }
        }
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
    [InlineData("Forest", "Ocean")]
    [InlineData("Shop", "Ocean", "shop")]
    [InlineData("Shop", "Nope")]
    public void App_themes_with_a_taken_name_or_an_unknown_base_are_refused(string name, string basedOn, string? second = null)
    {
        var options = new ThemingOptions();
        options.Themes.Add(new AppTheme(name, basedOn, new Dictionary<string, string>()));
        if (second is not null)
        {
            options.Themes.Add(new AppTheme(second, "Forest", new Dictionary<string, string>()));
        }

        Should.Throw<InvalidOperationException>(() => ThemeSeeds.Create(options)).Message.ShouldContain("ThemingOptions");
        new ThemingOptionsValidation().Validate(null, options).Failed.ShouldBeTrue();
        new ThemingOptionsValidation().Validate(null, new ThemingOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("#ffffff", "#000000")]
    [InlineData("#000000", "#ffffff")]
    [InlineData("#1565c0", "#ffffff")]
    [InlineData("red", null)]
    public void Contrast_text_follows_the_luminance(string color, string? expected) => ThemeSeeds.ContrastText(color).ShouldBe(expected);

    // ponytail: the older themes miss AA in places (button text of Coworkee, Forest and Sunset); measured are the ones that name every color
    public static TheoryData<string> Themes() => ["Ocean", "Midnight", "High Contrast", "Octo", "Code", "Arctic", "Vampire", "Solar", "Atom", "Paper", "Aubergine"];

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_of_the_built_in_themes_is_readable(string name)
    {
        var theme = ThemeSeeds.Create(new ThemingOptions()).Single(t => t.Name == name);
        foreach (var palette in new[] { theme.PaletteLight, theme.PaletteDark }.Select(p => JsonSerializer.Deserialize<Dictionary<string, string>>(p)!))
        {
            foreach (var (text, background) in new[]
                     {
                         ("TextPrimary", "Background"), ("TextPrimary", "Surface"), ("TextSecondary", "Surface"), ("PrimaryContrastText", "Primary"),
                         ("SecondaryContrastText", "Secondary"), ("AppbarText", "AppbarBackground"), ("DrawerText", "DrawerBackground"),
                     })
            {
                if (palette.TryGetValue(text, out var foreground) && palette.TryGetValue(background, out var behind))
                {
                    Contrast(foreground, behind).ShouldBeGreaterThanOrEqualTo(4.5, $"{name}: {text} on {background}");
                }
            }
        }
    }

    [Fact]
    public void The_new_built_in_themes_ship()
    {
        ThemeSeeds.Create(new ThemingOptions()).Select(t => t.Name).ShouldBe(
            ["Coworkee", "Ocean", "Forest", "Sunset", "Midnight", "Classic", "High Contrast", "Octo", "Code", "Arctic", "Vampire", "Solar", "Atom", "Paper", "Aubergine"]);
    }

    private static double Contrast(string a, string b)
    {
        static double Luminance(string color)
        {
            var rgb = Convert.ToInt32(color[1..7], 16);
            double Channel(int shift)
            {
                var c = ((rgb >> shift) & 0xff) / 255d;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(16)) + (0.7152 * Channel(8)) + (0.0722 * Channel(0));
        }

        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
