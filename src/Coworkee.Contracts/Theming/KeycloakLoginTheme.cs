using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Coworkee.Contracts.Theming;

/// <summary>What the Keycloak login theme shows; <see cref="LogoUrl"/> and the design's media address must be absolute web addresses, others are left out.</summary>
public sealed record KeycloakLoginThemeContent(string AppName, IReadOnlyDictionary<string, string> Light, IReadOnlyDictionary<string, string> Dark)
{
    /// <summary>The theme's logo (SVG markup or image data URL), a raster image is written as file next to the styles, SVG is embedded in them.</summary>
    public string? Logo { get; init; }

    /// <summary>The logo when <see cref="Logo"/> is empty.</summary>
    public string? LogoUrl { get; init; }

    public SignInDesign Design { get; init; } = new();
}

/// <summary>
/// Writes a Keycloak login theme in the app's design: a child of keycloak.v2 with the palette colors (light and dark), the logo, the app name and the
/// sign-in layout. Only styles, a script and messages, no template of its own, so it keeps working with newer Keycloak versions.
/// Used by the app host when Keycloak starts and by the API when the default theme or the sign-in design changes.
/// </summary>
public static partial class KeycloakLoginTheme
{
    /// <summary>Ocean, the default theme; the light text colors are MudBlazor's defaults.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultLight = new Dictionary<string, string>
    {
        ["Primary"] = "#1565c0", ["PrimaryContrastText"] = "#ffffff", ["Background"] = "#f4f7fb", ["Surface"] = "#ffffff",
        ["TextPrimary"] = "#424242ff", ["TextSecondary"] = "#00000089", ["LinesDefault"] = "#0000001e",
    };

    public static readonly IReadOnlyDictionary<string, string> DefaultDark = new Dictionary<string, string>
    {
        ["Primary"] = "#64b5f6", ["PrimaryContrastText"] = "#0b1a2a", ["Background"] = "#0f1722", ["Surface"] = "#16202e",
        ["TextPrimary"] = "#e3eefb", ["TextSecondary"] = "#9fb3c8", ["LinesDefault"] = "#26364a",
    };

    private static readonly (string Variable, string Palette)[] Colors =
    [
        ("--cw-primary", "Primary"), ("--cw-on-primary", "PrimaryContrastText"), ("--cw-background", "Background"), ("--cw-surface", "Surface"),
        ("--cw-text", "TextPrimary"), ("--cw-text-2", "TextSecondary"), ("--cw-lines", "LinesDefault"),
    ];

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/svg+xml"] = "svg", ["image/png"] = "png", ["image/jpeg"] = "jpg", ["image/webp"] = "webp",
    };

    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".ogg", ".ogv", ".mov"];

    /// <summary>The theme of an app: <c>coworkee-</c> and the app name in lower case.</summary>
    public static string NameFor(string app) => "coworkee-" + Unsafe().Replace(app.ToLowerInvariant(), "-");

    public static bool Exists(string themesDirectory, string name) => File.Exists(Path.Combine(themesDirectory, name, "login", "theme.properties"));

    /// <summary>Writes (or overwrites) <paramref name="themesDirectory"/>/<paramref name="name"/>/login; each file is written next to it first and then moved in place.</summary>
    public static void Write(string themesDirectory, string name, KeycloakLoginThemeContent content)
    {
        if (!ThemeName().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is no valid theme name.", nameof(name));
        }

        var login = Path.Combine(themesDirectory, name, "login");
        var images = Path.Combine(login, "resources", "img");
        // a raster logo is a file next to the styles; an SVG is embedded in them, so Keycloak never serves it as a document of its own origin
        string? file = null;
        string? logoAddress;
        if (ThemeLogo.TryRead(content.Logo, out var bytes, out var contentType))
        {
            if (ThemeLogo.IsSvg(contentType))
            {
                logoAddress = ThemeLogo.ToDataUrl(content.Logo);
            }
            else
            {
                file = "logo." + Extensions[contentType];
                Save(Path.Combine(images, file), bytes);
                logoAddress = $"../img/{file}";
            }
        }
        else
        {
            logoAddress = WebAddress(content.LogoUrl);
        }

        Save(Path.Combine(login, "theme.properties"), Properties);
        Save(Path.Combine(login, "resources", "css", "coworkee.css"), Css(content, logoAddress));
        Save(Path.Combine(login, "resources", "js", "coworkee.js"), Script(content));
        Save(Path.Combine(login, "messages", "messages_en.properties"), $"loginTitleHtml={MessageText(content.AppName)}\n");

        // only after the styles stopped pointing at them
        foreach (var extension in Extensions.Values.Where(e => Directory.Exists(images) && $"logo.{e}" != file))
        {
            File.Delete(Path.Combine(images, $"logo.{extension}"));
        }
    }

    private const string Properties = """
        parent=keycloak.v2
        import=common/keycloak
        styles=css/styles.css css/coworkee.css
        scripts=js/coworkee.js

        """;

    private static string Css(KeycloakLoginThemeContent content, string? logoAddress)
    {
        var css = new StringBuilder();
        css.Append(":root{").Append(Declarations(content.Light, DefaultLight)).Append("}\n");
        css.Append(":root.pf-v5-theme-dark{").Append(Declarations(content.Dark, DefaultDark)).Append("}\n");
        css.Append(StaticCss);
        if (logoAddress is not null)
        {
            css.Append("#kc-header-wrapper::before{content:\"\";width:100%;height:4rem;background:url(\"").Append(logoAddress).Append("\") no-repeat center/contain}\n");
        }

        return css.ToString();
    }

    private static string Script(KeycloakLoginThemeContent content)
    {
        var design = content.Design;
        var layout = design.Layout is { } named && SignInLayouts.All.FirstOrDefault(l => l.Equals(named, StringComparison.OrdinalIgnoreCase)) is { } known ? known : SignInLayouts.Default;
        var side = design.MediaSide ?? (layout == SignInLayouts.Slide ? SignInMediaSide.Right : SignInMediaSide.Left);
        var media = WebAddress(design.MediaUrl);
        var config = new JsonObject
        {
            ["layout"] = layout.ToLowerInvariant(),
            ["side"] = side.ToString().ToLowerInvariant(),
            ["media"] = media,
            ["video"] = media is not null && VideoExtensions.Any(e => media.Split('?', '#')[0].EndsWith(e, StringComparison.OrdinalIgnoreCase)),
            ["appName"] = content.AppName,
        };
        return $"var coworkee = {config.ToJsonString()};\n{StaticScript}";
    }

    private static string Declarations(IReadOnlyDictionary<string, string> palette, IReadOnlyDictionary<string, string> defaults) => string.Concat(Colors.Select(c =>
        $"{c.Variable}:{(palette.TryGetValue(c.Palette, out var value) && Color().IsMatch(value) ? value : defaults[c.Palette])};"));

    private static string? WebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri.AbsoluteUri : null;

    // the message is HTML and a MessageFormat pattern; the file is read as ISO-8859-1 by older Keycloak versions
    private static string MessageText(string appName)
    {
        var html = WebUtility.HtmlEncode(appName.ReplaceLineEndings(" ")).Replace("{", "'{'").Replace("}", "'}'");
        return string.Concat(html.Select(c => c switch
        {
            '\\' => "\\\\",
            > '~' => $"\\u{(int)c:x4}",
            _ => c.ToString(),
        }));
    }

    private static void Save(string path, string text) => Save(path, Encoding.UTF8.GetBytes(text));

    private static void Save(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, bytes);
        // on Windows a reader of the old file (Keycloak through the bind mount) blocks the replace for a moment
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
        }
    }

    private const string StaticCss = """
        :root{--pf-v5-global--primary-color--100:var(--cw-primary);--pf-v5-global--primary-color--200:color-mix(in srgb,var(--cw-primary) 80%,#000);
        --pf-v5-global--link--Color:var(--cw-primary);--pf-v5-global--link--Color--hover:var(--pf-v5-global--primary-color--200);
        --pf-v5-global--BackgroundColor--100:var(--cw-surface);--pf-v5-global--Color--100:var(--cw-text);--pf-v5-global--Color--200:var(--cw-text-2);
        --pf-v5-global--BorderColor--100:var(--cw-lines);--keycloak-card-top-color:var(--cw-primary)}
        .login-pf body{background:var(--cw-background);color:var(--cw-text)}
        .login-pf .pf-v5-c-login{--pf-v5-c-login__main--BackgroundColor:var(--cw-surface)}
        .pf-v5-c-button{--pf-v5-c-button--m-primary--Color:var(--cw-on-primary);--pf-v5-c-button--m-primary--hover--Color:var(--cw-on-primary);
        --pf-v5-c-button--m-primary--focus--Color:var(--cw-on-primary);--pf-v5-c-button--m-primary--active--Color:var(--cw-on-primary)}
        #kc-header-wrapper{color:var(--cw-text)!important;text-transform:none;letter-spacing:0;display:flex;flex-direction:column;align-items:center;gap:.75rem}
        .cw-media{display:none}
        @media (min-width:768px){
        .cw-layout-split .cw-media,.cw-layout-slide .cw-media{display:flex;position:fixed;inset:0 auto 0 0;width:50vw;overflow:hidden;align-items:center;justify-content:center;
        background:var(--cw-primary) center/cover no-repeat;color:var(--cw-on-primary)}
        .cw-layout-split .cw-media{background-color:var(--cw-background)}
        .cw-side-right .cw-media{inset:0 0 0 auto}
        .cw-media video{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
        .cw-media h2{position:relative;font-size:2.5rem;padding:2rem;text-align:center}
        .cw-media.cw-has-media h2{color:#fff;text-shadow:0 2px 12px rgba(0,0,0,.6)}
        .cw-layout-split .pf-v5-c-login,.cw-layout-slide .pf-v5-c-login{margin-left:50vw}
        .cw-side-right.cw-layout-split .pf-v5-c-login,.cw-side-right.cw-layout-slide .pf-v5-c-login{margin-left:0;margin-right:50vw}
        }

        """;

    private const string StaticScript = """
        (function () {
          var root = document.documentElement;
          root.classList.add("cw-layout-" + coworkee.layout, "cw-side-" + coworkee.side);
          if (coworkee.layout === "card") {
            return;
          }

          document.addEventListener("DOMContentLoaded", function () {
            // the registration shows the panel on the other side, like the app's own sign-in pages
            if (coworkee.layout === "slide" && document.body.dataset.pageId === "login-register") {
              root.classList.replace("cw-side-" + coworkee.side, "cw-side-" + (coworkee.side === "left" ? "right" : "left"));
            }

            var aside = document.createElement("aside");
            aside.className = coworkee.media ? "cw-media cw-has-media" : "cw-media";
            aside.setAttribute("aria-hidden", "true");
            if (coworkee.media && coworkee.video) {
              var video = document.createElement("video");
              video.src = coworkee.media;
              video.autoplay = video.muted = video.loop = video.playsInline = true;
              aside.appendChild(video);
            } else if (coworkee.media) {
              aside.style.backgroundImage = "url(" + JSON.stringify(coworkee.media) + ")";
            }

            if (coworkee.layout === "slide") {
              var name = document.createElement("h2");
              name.textContent = coworkee.appName;
              aside.appendChild(name);
            }

            document.body.prepend(aside);
          });
        })();

        """;

    [GeneratedRegex(@"^(#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|(rgb|rgba|hsl|hsla)\(\s*[0-9.]+%?\s*(,\s*[0-9.]+%?\s*){2,3}\)|transparent)$")]
    private static partial Regex Color();

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex Unsafe();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,80}$")]
    private static partial Regex ThemeName();
}
