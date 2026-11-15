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

        Read("resources/css/coworkee.css").ShouldNotContain("#kc-header-wrapper::before{content:");
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
        config.GetProperty("background").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("https://app.example/media/sky.webm", 140, "https://app.example/media/sky.webm", true, 90)]
    [InlineData("https://app.example/sky.jpg", null, "https://app.example/sky.jpg", false, 0)]
    [InlineData("javascript:alert(1)", 30, null, false, 30)]
    public void The_script_gets_the_page_background_and_its_darkening(string background, int? dim, string? expected, bool video, int expectedDim)
    {
        Write(Content() with { Design = new SignInDesign { Layout = "Card", BackgroundUrl = background, BackgroundDim = dim } });

        var script = Read("resources/js/coworkee.js");
        var config = JsonDocument.Parse(script["var coworkee = ".Length..script.IndexOf(";\n", StringComparison.Ordinal)]).RootElement;
        config.GetProperty("background").GetString().ShouldBe(expected);
        config.GetProperty("backgroundVideo").GetBoolean().ShouldBe(video);
        config.GetProperty("backgroundDim").GetInt32().ShouldBe(expectedDim);
        Read("resources/css/coworkee.css").ShouldContain(".cw-background{position:fixed");
    }

    [Theory]
    [InlineData("Glass", true, false)]
    [InlineData("aurora", false, true)]
    [InlineData("Card", false, false)]
    public void Stored_glass_and_aurora_designs_reach_the_script_as_the_card_with_surface_and_animation(string layout, bool glass, bool aurora)
    {
        Write(Content() with { Design = new SignInDesign { Layout = layout } });

        var script = Read("resources/js/coworkee.js");
        var css = Read("resources/css/coworkee.css");
        Config(script).ShouldSatisfyAllConditions(
            c => c.GetProperty("layout").GetString().ShouldBe("card"),
            c => c.GetProperty("glass").GetBoolean().ShouldBe(glass),
            c => c.GetProperty("aurora").GetBoolean().ShouldBe(aurora));
        css.ShouldContain(".cw-surface-glass .pf-v5-c-login__main,.cw-surface-glass.cw-layout-spotlight .pf-v5-c-login__container{background:color-mix(in srgb,var(--cw-surface) 62%,transparent)");
        css.ShouldContain("backdrop-filter:blur(22px)");
        css.ShouldContain(".cw-aurora span:nth-child(3){background:var(--cw-aurora-3,var(--cw-tertiary))");
        css.ShouldContain("@media (prefers-reduced-motion:reduce){.cw-aurora span,.cw-background,.cw-media");
        // the secondary and tertiary colors the backgrounds mix, light and dark
        css.ShouldContain("--cw-secondary:#00838f;--cw-tertiary:#1ec8a5;");
        css.ShouldContain("--cw-secondary:#4dd0e1;--cw-tertiary:#1ec8a5;");
        script.ShouldContain("aurora.className = \"cw-aurora\";");
    }

    [Fact]
    public void The_glass_card_lies_over_the_media_when_there_is_no_page_background()
    {
        Write(Content() with { Design = new SignInDesign { Layout = "Glass", MediaUrl = "https://app.example/media/sea.mp4" } });
        Config(Read("resources/js/coworkee.js")).ShouldSatisfyAllConditions(
            c => c.GetProperty("background").GetString().ShouldBe("https://app.example/media/sea.mp4"),
            c => c.GetProperty("backgroundVideo").GetBoolean().ShouldBeTrue());

        Write(Content() with { Design = new SignInDesign { Layout = "Glass", MediaUrl = "https://app.example/sea.jpg", BackgroundUrl = "https://app.example/sky.jpg" } });
        Config(Read("resources/js/coworkee.js")).GetProperty("background").GetString().ShouldBe("https://app.example/sky.jpg");

        // the card has no media of its own
        Write(Content() with { Design = new SignInDesign { Layout = "Aurora", MediaUrl = "https://app.example/sea.jpg" } });
        Config(Read("resources/js/coworkee.js")).ShouldSatisfyAllConditions(
            c => c.GetProperty("background").ValueKind.ShouldBe(JsonValueKind.Null),
            c => c.GetProperty("media").ValueKind.ShouldBe(JsonValueKind.Null));
    }

    [Theory]
    [InlineData("Spotlight", "right")]
    [InlineData("Poster", "left")]
    [InlineData("Stack", "left")]
    [InlineData("Minimal", "left")]
    public void Every_layout_reaches_the_script_and_has_its_styles(string layout, string side)
    {
        Write(Content() with { Design = new SignInDesign { Layout = layout, LogoSize = SignInLogoSize.Large } });

        var config = Config(Read("resources/js/coworkee.js"));
        var css = Read("resources/css/coworkee.css");
        config.GetProperty("layout").GetString().ShouldBe(layout.ToLowerInvariant());
        config.GetProperty("side").GetString().ShouldBe(side);
        // only the minimal layout sizes the logo
        config.GetProperty("logoSize").ValueKind.ShouldBe(layout == "Minimal" ? JsonValueKind.String : JsonValueKind.Null);
        css.ShouldContain($".cw-layout-{layout.ToLowerInvariant()}");
    }

    [Fact]
    public void The_script_adds_the_poster_spotlight_caption_and_stack_parts()
    {
        Write(Content() with { Design = new SignInDesign { Layout = SignInLayouts.Stack, MediaUrl = "https://app.example/cards.jpg" } });

        var script = Read("resources/js/coworkee.js");

        Config(script).GetProperty("media").GetString().ShouldBe("https://app.example/cards.jpg");
        script.ShouldContain("caption.className = \"cw-caption\";");
        script.ShouldContain("root.classList.add(\"cw-flipped\");");
        script.ShouldContain("main.style.setProperty(\"--cw-media\"");
        script.ShouldContain("var claim = coworkee.layout === \"poster\" ? pick(coworkee.title) : null;");
    }

    [Fact]
    public void Fills_become_variables_of_the_styles_with_the_theme_colors_and_the_script_knows_of_them()
    {
        Write(Content() with
        {
            Design = new SignInDesign
            {
                Layout = SignInLayouts.Stack, Tilt = 30, Animation = SignInAnimation.Aurora,
                PageFill = new SignInFill { Gradient = new SignInGradient { Kind = SignInGradientKind.Radial, Animated = true, Stops = [new() { Color = "#000000", Position = 0 }, new() { Color = "secondary", Position = 100 }] } },
                PanelFill = new SignInFill { Color = "rgb(10, 20, 30)" },
            },
        });

        var css = Read("resources/css/coworkee.css");
        var config = Config(Read("resources/js/coworkee.js"));

        css.ShouldContain(":root{--cw-page-fill:radial-gradient(circle at 50% 50%, #000000 0%, var(--cw-secondary) 100%);--cw-aurora-1:#000000;--cw-aurora-2:#000000;--cw-aurora-3:var(--cw-secondary);"
            + "--cw-panel-fill:rgb(10, 20, 30);--cw-tilt:12deg;}");
        config.GetProperty("fill").GetBoolean().ShouldBeTrue();
        config.GetProperty("fillAnimated").GetBoolean().ShouldBeTrue();
        config.GetProperty("aurora").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData("red;}body{display:none}")]
    [InlineData("url(https://evil.test/x.png)")]
    [InlineData("rgb(1,2,3)}*{x:y")]
    [InlineData("</style><script>alert(1)</script>")]
    public void Hostile_fill_colors_never_reach_the_styles(string color)
    {
        Write(Content() with
        {
            Design = new SignInDesign
            {
                Layout = SignInLayouts.Slide, PageFill = new SignInFill { Color = color },
                PanelFill = new SignInFill { Gradient = new SignInGradient { Stops = [new() { Color = color, Position = 0 }, new() { Color = color, Position = 100 }] } },
            },
        });

        var css = Read("resources/css/coworkee.css");
        css.ShouldNotContain("--cw-page-fill:");
        css.ShouldNotContain("--cw-panel-fill:");
        css.ShouldNotContain("evil.test");
        css.ShouldNotContain("<script>");
        Config(Read("resources/js/coworkee.js")).GetProperty("fill").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Palette_secondary_and_tertiary_colors_replace_the_defaults()
    {
        Write(Content() with { Light = new Dictionary<string, string> { ["Secondary"] = "#111111", ["Tertiary"] = "#222222" } });

        Read("resources/css/coworkee.css").ShouldContain("--cw-secondary:#111111;--cw-tertiary:#222222;");
    }

    [Fact]
    public void The_header_title_is_the_app_name_as_escaped_html_message()
    {
        Write(Content() with { AppName = "Mein {Büro} & 東京" });

        Read("messages/messages_en.properties").ShouldBe("loginTitleHtml=Mein '{'B&#252;ro'}' &amp; \\u6771\\u4eac\n");
    }

    [Fact]
    public void The_script_gets_the_own_texts_per_language_and_without_the_app_name_the_header_stays_empty()
    {
        Write(Content() with
        {
            Design = new SignInDesign
            {
                ShowAppName = false,
                Texts = new SignInTexts { Title = "Welcome", Titles = { ["de-AT"] = "Servus", ["fr"] = " " }, Notice = "<b>Maintenance</b>" },
            },
        });

        var script = Read("resources/js/coworkee.js");
        var config = JsonDocument.Parse(script["var coworkee = ".Length..script.IndexOf(";\n", StringComparison.Ordinal)]).RootElement;
        config.GetProperty("title").GetProperty("").GetString().ShouldBe("Welcome");
        config.GetProperty("title").GetProperty("de-at").GetString().ShouldBe("Servus");
        config.GetProperty("title").TryGetProperty("fr", out _).ShouldBeFalse();
        config.GetProperty("subtitle").ValueKind.ShouldBe(JsonValueKind.Null);
        config.GetProperty("notice").GetProperty("").GetString().ShouldBe("<b>Maintenance</b>"); // the script sets it as text
        config.GetProperty("appName").ValueKind.ShouldBe(JsonValueKind.Null);
        script.ShouldContain("line.textContent = text;");
        script.ShouldNotContain("innerHTML");
        Read("messages/messages_en.properties").ShouldBe("loginTitleHtml=\n");
    }

    [Fact]
    public void Rewriting_leaves_no_temporary_files()
    {
        Write(Content());
        Write(Content());

        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public void Keycloak_running_as_another_user_can_read_the_theme_even_from_a_private_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes only.");
        File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Write(Content());

        File.GetUnixFileMode(_root).HasFlag(UnixFileMode.OtherRead | UnixFileMode.OtherExecute).ShouldBeTrue();
        File.GetUnixFileMode(Path.Combine(Login, "resources", "css")).HasFlag(UnixFileMode.OtherRead | UnixFileMode.OtherExecute).ShouldBeTrue();
        File.GetUnixFileMode(Path.Combine(Login, "resources", "css", "coworkee.css")).HasFlag(UnixFileMode.OtherRead).ShouldBeTrue();
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

    private static JsonElement Config(string script) => JsonDocument.Parse(script["var coworkee = ".Length..script.IndexOf(";\n", StringComparison.Ordinal)]).RootElement;
}
