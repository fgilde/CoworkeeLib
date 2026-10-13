using System.Text.Json;
using Coworkee.Contracts.Theming;

namespace Coworkee.Theming.Tests;

public sealed class KeycloakLoginThemeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("keycloak-themes-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void The_theme_extends_keycloak_v2_with_its_own_styles_and_script()
    {
        Write(Content());

        var properties = Read("theme.properties");
        properties.ShouldContain("parent=keycloak.v2");
        properties.ShouldContain("styles=css/styles.css css/coworkee.css");
        properties.ShouldContain("scripts=js/coworkee.js");
        KeycloakLoginTheme.Exists(_root, "coworkee-myapp").ShouldBeTrue();
    }

    [Fact]
    public void The_styles_carry_the_palette_colors_light_and_dark_and_fill_gaps_with_ocean()
    {
        Write(Content() with
        {
            Light = new Dictionary<string, string> { ["Primary"] = "#0a7d5a", ["Surface"] = "red;}body{x:y" },
            Dark = new Dictionary<string, string> { ["Primary"] = "hsl(120, 40%, 60%)", ["Background"] = "rgba(1, 2, 3, 0.5)", ["Surface"] = "transparent" },
        });

        var css = Read("resources/css/coworkee.css");
        css.ShouldContain(":root{--cw-primary:#0a7d5a;--cw-on-primary:#ffffff;--cw-background:#f4f7fb;--cw-surface:#ffffff;");
        css.ShouldContain(":root.pf-v5-theme-dark{--cw-primary:hsl(120, 40%, 60%);--cw-on-primary:#0b1a2a;--cw-background:rgba(1, 2, 3, 0.5);--cw-surface:transparent;");
        css.ShouldContain("--pf-v5-global--primary-color--100:var(--cw-primary)");
        css.ShouldNotContain("body{x:y");
    }

    [Fact]
    public void A_raster_logo_becomes_a_file_the_styles_show_in_the_header()
    {
        Write(Content() with { Logo = "data:image/png;base64,AQID", LogoUrl = "https://app.example/logo.svg" });

        File.ReadAllBytes(Path.Combine(Login, "resources", "img", "logo.png")).ShouldBe([1, 2, 3]);
        Read("resources/css/coworkee.css").ShouldContain("#kc-header-wrapper::before{content:\"\";width:100%;height:4rem;background:url(\"../img/logo.png\")");
    }

    [Fact]
    public void An_svg_logo_is_embedded_in_the_styles_and_not_served_as_file()
    {
        Write(Content() with { Logo = "<svg viewBox=\"0 0 1 1\"></svg>" });

        Directory.Exists(Path.Combine(Login, "resources", "img")).ShouldBeFalse();
        Read("resources/css/coworkee.css").ShouldContain($"background:url(\"{ThemeLogo.ToDataUrl("<svg viewBox=\"0 0 1 1\"></svg>")}\")");
    }

    [Fact]
    public void Without_a_theme_logo_the_logo_address_shows_and_older_logo_files_go_but_other_files_stay()
    {
        Write(Content() with { Logo = "data:image/png;base64,AQID" });
        var images = Path.Combine(Login, "resources", "img");
        File.WriteAllText(Path.Combine(images, "logo.webp"), "old");
        File.WriteAllText(Path.Combine(images, "logo.backup.txt"), "mine");

        Write(Content() with { LogoUrl = "https://app.example/logo.svg" });

        Directory.GetFiles(images).Select(Path.GetFileName).ShouldBe(["logo.backup.txt"]);
        Read("resources/css/coworkee.css").ShouldContain("url(\"https://app.example/logo.svg\")");
    }
    [Fact]
    public void A_logo_address_that_is_no_web_address_is_left_out()
    {
        Write(Content() with { LogoUrl = "javascript:alert(1)" });

        Read("resources/css/coworkee.css").ShouldNotContain("kc-header-wrapper::before");
    }

    [Theory]
    [InlineData(null, null, null, "slide", "right", null, false)]
    [InlineData("Card", null, null, "card", "left", null, false)]
    [InlineData("split", "https://app.example/media/welcome.mp4?v=1", null, "split", "left", "https://app.example/media/welcome.mp4?v=1", true)]
    [InlineData("Slide", "https://app.example/hero.jpg", SignInMediaSide.Left, "slide", "left", "https://app.example/hero.jpg", false)]
    [InlineData("Brand", "/media/hero.jpg", null, "slide", "right", null, false)]
    public void The_script_gets_the_layout_side_and_media(string? layout, string? media, SignInMediaSide? side, string expectedLayout, string expectedSide, string? expectedMedia, bool video)
    {
        Write(Content() with { Design = new SignInDesign { Layout = layout, MediaUrl = media, MediaSide = side } });

        var script = Read("resources/js/coworkee.js");
        var config = JsonDocument.Parse(script["var coworkee = ".Length..script.IndexOf(";\n", StringComparison.Ordinal)]).RootElement;
        config.GetProperty("layout").GetString().ShouldBe(expectedLayout);
        config.GetProperty("side").GetString().ShouldBe(expectedSide);
        config.GetProperty("media").GetString().ShouldBe(expectedMedia);
        config.GetProperty("video").GetBoolean().ShouldBe(video);
        config.GetProperty("appName").GetString().ShouldBe("Mein <Büro>");
    }

    [Fact]
    public void The_header_title_is_the_app_name_as_escaped_html_message()
    {
        Write(Content() with { AppName = "Mein {Büro} & 東京" });

        Read("messages/messages_en.properties").ShouldBe("loginTitleHtml=Mein '{'B&#252;ro'}' &amp; \\u6771\\u4eac\n");
    }

    [Fact]
    public void Rewriting_leaves_no_temporary_files()
    {
        Write(Content());
        Write(Content());

        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("Upper")]
    [InlineData("")]
    public void A_theme_name_that_is_no_plain_folder_name_is_refused(string name) =>
        Should.Throw<ArgumentException>(() => KeycloakLoginTheme.Write(_root, name, Content()));

    [Fact]
    public void The_theme_name_derives_from_the_app_name() => KeycloakLoginTheme.NameFor("My App").ShouldBe("coworkee-my-app");

    private string Login => Path.Combine(_root, "coworkee-myapp", "login");

    private static KeycloakLoginThemeContent Content() => new("Mein <Büro>", KeycloakLoginTheme.DefaultLight, KeycloakLoginTheme.DefaultDark);

    private void Write(KeycloakLoginThemeContent content) => KeycloakLoginTheme.Write(_root, "coworkee-myapp", content);

    private string Read(string path) => File.ReadAllText(Path.Combine(Login, path));
}
