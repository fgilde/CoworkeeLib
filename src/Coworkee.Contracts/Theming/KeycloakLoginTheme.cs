using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Coworkee.Contracts.Theming;

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
        ["TextPrimary"] = "#424242ff", ["TextSecondary"] = "#00000089", ["LinesDefault"] = "#0000001e", ["Secondary"] = "#00838f", ["Tertiary"] = "#1ec8a5",
    };

    public static readonly IReadOnlyDictionary<string, string> DefaultDark = new Dictionary<string, string>
    {
        ["Primary"] = "#64b5f6", ["PrimaryContrastText"] = "#0b1a2a", ["Background"] = "#0f1722", ["Surface"] = "#16202e",
        ["TextPrimary"] = "#e3eefb", ["TextSecondary"] = "#9fb3c8", ["LinesDefault"] = "#26364a", ["Secondary"] = "#4dd0e1", ["Tertiary"] = "#1ec8a5",
    };

    private static readonly (string Variable, string Palette)[] Colors =
    [
        ("--cw-primary", "Primary"), ("--cw-on-primary", "PrimaryContrastText"), ("--cw-background", "Background"), ("--cw-surface", "Surface"),
        ("--cw-text", "TextPrimary"), ("--cw-text-2", "TextSecondary"), ("--cw-lines", "LinesDefault"), ("--cw-secondary", "Secondary"), ("--cw-tertiary", "Tertiary"),
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
        Save(Path.Combine(login, "messages", "messages_en.properties"), $"loginTitleHtml={(content.Design.ShowAppName ? MessageText(content.AppName) : string.Empty)}\n");

        // only after the styles stopped pointing at them
        foreach (var extension in Extensions.Values.Where(e => Directory.Exists(images) && $"logo.{e}" != file))
        {
            File.Delete(Path.Combine(images, $"logo.{extension}"));
        }

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(themesDirectory, name), "*", SearchOption.AllDirectories).Prepend(Path.Combine(themesDirectory, name)).Prepend(themesDirectory))
        {
            Readable(directory, ReadableDirectory);
        }
    }

    private const UnixFileMode ReadableFile = UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode ReadableDirectory = ReadableFile | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    // Keycloak reads the mounted folder as its own user (uid 1000): outside Windows a 0700 temp folder or a strict umask would hide the theme
    private static void Readable(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & mode) != mode)
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | mode);
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
        if (Variables(Look(content.Design)) is { Length: > 0 } variables)
        {
            css.Append(":root{").Append(variables).Append("}\n");
        }

        if (logoAddress is not null)
        {
            css.Append("#kc-header-wrapper::before{content:\"\";width:100%;height:4rem;background:url(\"").Append(logoAddress).Append("\") no-repeat center/contain}\n");
        }

        return css.ToString();
    }

    // the former layouts Glass and Aurora as the card they became; an app's own layout name is unknown here and falls back to the default
    private static SignInDesign Look(SignInDesign design)
    {
        var look = design.With(design);
        look.Normalize();
        look.Layout = SignInLayouts.All.FirstOrDefault(l => l.Equals(look.Layout, StringComparison.OrdinalIgnoreCase)) ?? SignInLayouts.Default;
        return look;
    }

    // the fills, tilt and aurora colors as variables, built from checked values only (SignInFills)
    private static string Variables(SignInDesign look)
    {
        var options = SignInLayoutOptions.For(look.Layout);
        var variables = new StringBuilder();
        if (WebAddress(look.BackgroundUrl) is null && SignInFills.Css(look.PageFill, ThemeColor) is { } page)
        {
            variables.Append("--cw-page-fill:").Append(page).Append(';');
            if (options.Animation && look.Animation == SignInAnimation.Aurora)
            {
                var colors = SignInFills.ColorsOf(look.PageFill, ThemeColor);
                for (var i = 0; i < 3; i++)
                {
                    variables.Append($"--cw-aurora-{i + 1}:").Append(colors[(i * Math.Max(colors.Count - 1, 1) / 2) % colors.Count]).Append(';');
                }
            }
        }

        if (options.PanelBackground is not null && WebAddress(look.MediaUrl) is null && SignInFills.Css(look.PanelFill, ThemeColor) is { } panel)
        {
            variables.Append("--cw-panel-fill:").Append(panel).Append(';');
        }

        if (options.Tilt)
        {
            variables.Append("--cw-tilt:").Append(Math.Clamp(look.Tilt ?? SignInLayoutOptions.DefaultTilt, 0, SignInLayoutOptions.MaxTilt)).Append("deg;");
        }

        return variables.ToString();
    }

    private static string ThemeColor(string color) => color switch
    {
        "primary" => "var(--cw-primary)",
        "secondary" => "var(--cw-secondary)",
        "tertiary" => "var(--cw-tertiary)",
        "background" => "var(--cw-background)",
        _ => "var(--cw-surface)",
    };

    private static string Script(KeycloakLoginThemeContent content)
    {
        var design = Look(content.Design);
        var layout = design.Layout!;
        var options = SignInLayoutOptions.For(layout);
        var side = design.MediaSide ?? (layout is SignInLayouts.Slide or SignInLayouts.Spotlight ? SignInMediaSide.Right : SignInMediaSide.Left);
        var media = options.PanelBackground is null ? null : WebAddress(design.MediaUrl);
        var background = WebAddress(design.BackgroundUrl);
        var config = new JsonObject
        {
            ["layout"] = layout.ToLowerInvariant(),
            ["side"] = side.ToString().ToLowerInvariant(),
            ["media"] = media,
            ["video"] = IsVideo(media),
            ["background"] = background,
            ["backgroundVideo"] = IsVideo(background),
            ["backgroundDim"] = options.Dim ? Math.Clamp(design.BackgroundDim ?? 0, 0, 90) : 0,
            ["fill"] = background is null && SignInFills.Checked(design.PageFill) is not null,
            ["fillAnimated"] = background is null && SignInFills.IsAnimated(design.PageFill),
            ["panelAnimated"] = media is null && options.PanelBackground is not null && SignInFills.IsAnimated(design.PanelFill),
            ["glass"] = options.Surface && design.Surface == SignInSurface.Glass,
            ["aurora"] = options.Animation && design.Animation == SignInAnimation.Aurora,
            ["logoSize"] = options.LogoSize ? (design.LogoSize ?? SignInLogoSize.Medium).ToString().ToLowerInvariant() : null,
            ["appName"] = design.ShowAppName ? content.AppName : null,
            ["title"] = Texts(design.Texts.Title, design.Texts.Titles),
            ["subtitle"] = Texts(design.Texts.Subtitle, design.Texts.Subtitles),
            ["notice"] = Texts(design.Texts.Notice, design.Texts.Notices),
        };
        return $"var coworkee = {config.ToJsonString()};\n{StaticScript}";
    }

    // culture to text, "" for every language; the script sets them as text, never as markup
    private static JsonObject? Texts(string? text, Dictionary<string, string>? perCulture)
    {
        var texts = new JsonObject();
        foreach (var (culture, value) in (perCulture ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Value)).Prepend(new(string.Empty, text ?? string.Empty)))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                texts[culture.ToLowerInvariant()] = value.Trim();
            }
        }

        return texts.Count == 0 ? null : texts;
    }

    private static string Declarations(IReadOnlyDictionary<string, string> palette, IReadOnlyDictionary<string, string> defaults) => string.Concat(Colors.Select(c =>
        $"{c.Variable}:{(palette.TryGetValue(c.Palette, out var value) && Color().IsMatch(value) ? value : defaults[c.Palette])};"));

    private static bool IsVideo(string? url) => url is not null && VideoExtensions.Any(e => url.Split('?', '#')[0].EndsWith(e, StringComparison.OrdinalIgnoreCase));

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
        Readable(temp, ReadableFile);
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
        .cw-has-background{background:var(--cw-background)}
        .cw-has-background body,.cw-has-background .pf-v5-c-login{background:transparent}
        .cw-background{position:fixed;inset:0;z-index:-1;overflow:hidden;background:var(--cw-page-fill,transparent) center/cover no-repeat}
        .cw-fill-animated .cw-background,.cw-panel-animated .cw-media,.cw-panel-animated.cw-layout-stack .pf-v5-c-login__main::before{background-size:300% 300%;animation:cw-fill 22s ease-in-out infinite alternate}
        @keyframes cw-fill{from{background-position:0% 30%}to{background-position:100% 70%}}
        .cw-background video{width:100%;height:100%;object-fit:cover}
        .cw-background::after{content:"";position:absolute;inset:0;background:rgb(0 0 0 / var(--cw-dim,0%))}
        .cw-subtitle{margin:.25rem 0 0;color:var(--cw-text-2)}
        .cw-notice{margin:1rem 0 0;padding:.6rem .75rem;border-left:3px solid var(--cw-primary);white-space:pre-line;font-size:.875rem}
        .cw-transparent body,.cw-transparent .pf-v5-c-login{background:transparent}
        .cw-surface-glass:not(.cw-has-background){background:radial-gradient(60vmax 60vmax at 12% 18%,color-mix(in srgb,var(--cw-primary) 55%,transparent),transparent 70%),
        radial-gradient(55vmax 55vmax at 88% 85%,color-mix(in srgb,var(--cw-tertiary) 45%,transparent),transparent 70%),
        linear-gradient(135deg,color-mix(in srgb,var(--cw-primary) 18%,var(--cw-background)),var(--cw-background));min-height:100%}
        .cw-surface-glass .pf-v5-c-login__main,.cw-surface-glass.cw-layout-spotlight .pf-v5-c-login__container{background:color-mix(in srgb,var(--cw-surface) 62%,transparent);border:1px solid color-mix(in srgb,var(--cw-text) 12%,transparent);
        border-radius:1.25rem;box-shadow:0 24px 60px rgba(0,0,0,.18),inset 0 1px 0 rgba(255,255,255,.18);-webkit-backdrop-filter:blur(22px) saturate(160%);backdrop-filter:blur(22px) saturate(160%)}
        .cw-surface-glass.cw-layout-spotlight .pf-v5-c-login__main{background:transparent;border:0;box-shadow:none;-webkit-backdrop-filter:none;backdrop-filter:none}
        .cw-surface-glass .pf-v5-c-login__main .pf-v5-c-form-control{background:color-mix(in srgb,var(--cw-surface) 70%,transparent)}
        .cw-with-aurora.cw-layout-card .pf-v5-c-login__main{border:1px solid var(--cw-lines);border-radius:1.25rem;box-shadow:0 30px 80px color-mix(in srgb,var(--cw-primary) 22%,rgba(0,0,0,.12))}
        .cw-aurora{position:fixed;inset:0;z-index:-1;overflow:hidden;pointer-events:none}
        .cw-aurora span{position:absolute;width:64vmax;height:64vmax;border-radius:50%;filter:blur(80px);opacity:.45;will-change:transform}
        .cw-aurora span:nth-child(1){background:var(--cw-aurora-1,var(--cw-primary));top:-26vmax;left:-18vmax;animation:cw-aurora-a 18s ease-in-out infinite alternate}
        .cw-aurora span:nth-child(2){background:var(--cw-aurora-2,var(--cw-secondary));bottom:-30vmax;right:-22vmax;opacity:.3;animation:cw-aurora-b 23s ease-in-out infinite alternate}
        .cw-aurora span:nth-child(3){background:var(--cw-aurora-3,var(--cw-tertiary));width:44vmax;height:44vmax;top:34%;left:38%;opacity:.35;animation:cw-aurora-c 27s ease-in-out infinite alternate}
        @keyframes cw-aurora-a{to{transform:translate(22vmax,14vmax) scale(1.15)}}
        @keyframes cw-aurora-b{to{transform:translate(-20vmax,-12vmax) scale(1.1)}}
        @keyframes cw-aurora-c{to{transform:translate(-16vmax,10vmax) scale(.85)}}
        .cw-layout-spotlight:not(.cw-has-background){background:radial-gradient(90vmax 70vmax at 0% 100%,color-mix(in srgb,var(--cw-primary) 85%,transparent),transparent 62%),
        radial-gradient(60vmax 60vmax at 55% -10%,color-mix(in srgb,var(--cw-tertiary) 55%,transparent),transparent 60%),
        linear-gradient(150deg,color-mix(in srgb,var(--cw-primary) 45%,var(--cw-background)),color-mix(in srgb,var(--cw-secondary) 30%,var(--cw-background)));min-height:100%}
        .cw-layout-spotlight .pf-v5-c-login__container{background:var(--cw-surface);border-radius:1.75rem;box-shadow:0 30px 90px rgba(0,0,0,.28);padding:2rem 1.5rem}
        .cw-layout-spotlight .pf-v5-c-login__main{box-shadow:none;background:transparent}
        .cw-caption{position:relative;margin:0;padding:15vh 1.5rem 1.5rem;color:#fff;font-size:2.75rem;font-weight:800;line-height:.95;letter-spacing:-.035em;text-shadow:0 2px 30px rgba(0,0,0,.35)}
        .cw-layout-stack .pf-v5-c-login__main{position:relative;border-radius:1.25rem}
        .cw-layout-stack .pf-v5-c-login__main::before,.cw-layout-stack .pf-v5-c-login__main::after{content:"";position:absolute;inset:0;z-index:-1;border-radius:inherit;box-shadow:0 20px 50px rgba(0,0,0,.18);
        background:var(--cw-media,var(--cw-panel-fill,linear-gradient(135deg,var(--cw-primary),var(--cw-tertiary)))) center/cover no-repeat;transform:rotate(var(--cw-tilt,6deg)) translate(5%,1%)}
        .cw-layout-stack .pf-v5-c-login__main::after{background:var(--cw-panel-fill,linear-gradient(135deg,var(--cw-primary),var(--cw-tertiary)));z-index:-2;opacity:.7;filter:brightness(.8);transform:rotate(calc(var(--cw-tilt,6deg) * -1.5)) translate(-7%,3%)}
        .cw-flipped.cw-layout-stack .pf-v5-c-login__main::before{transform:rotate(calc(var(--cw-tilt,6deg) * -1)) translate(-5%,1%)}
        .cw-flipped.cw-layout-stack .pf-v5-c-login__main::after{transform:rotate(calc(var(--cw-tilt,6deg) * 1.5)) translate(7%,3%)}
        .cw-layout-stack body{overflow-x:hidden}
        .cw-layout-minimal .pf-v5-c-login__main{background:transparent;box-shadow:none;border:0}
        .cw-layout-minimal #kc-header-wrapper::before{height:var(--cw-logo,5rem)!important}
        .cw-logo-small{--cw-logo:3rem}.cw-logo-large{--cw-logo:7.5rem}
        .cw-layout-poster .pf-v5-c-form-control{--pf-v5-c-form-control--BorderRadius:0;border-radius:0}
        .cw-layout-poster .pf-v5-c-login__main{box-shadow:none;background:transparent}
        .cw-media{display:none}
        @media (min-width:768px){
        .cw-layout-split .cw-media,.cw-layout-slide .cw-media,.cw-layout-poster .cw-media{display:flex;position:fixed;inset:0 auto 0 0;width:50vw;overflow:hidden;align-items:center;justify-content:center;
        background:var(--cw-panel-fill,var(--cw-primary)) center/cover no-repeat;color:var(--cw-on-primary)}
        .cw-layout-split .cw-media{background:var(--cw-panel-fill,var(--cw-background)) center/cover no-repeat}
        .cw-layout-poster .cw-media{flex-direction:column;align-items:flex-start;justify-content:flex-end;padding:4rem;background:var(--cw-panel-fill,linear-gradient(160deg,var(--cw-primary),color-mix(in srgb,var(--cw-primary) 60%,#000))) center/cover no-repeat}
        .cw-side-right .cw-media{inset:0 0 0 auto}
        .cw-media video{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
        .cw-media h2{position:relative;font-size:2.5rem;padding:2rem;text-align:center}
        .cw-layout-poster .cw-media h2{padding:0;text-align:left;font-size:clamp(3.5rem,8vw,9rem);font-weight:800;line-height:.88;letter-spacing:-.05em}
        .cw-media p{position:relative;margin:1.5rem 0 0;max-width:20ch;padding-top:1.25rem;border-top:3px solid currentColor;font-size:1.75rem;font-weight:600;line-height:1.2}
        .cw-media.cw-has-media h2,.cw-media.cw-has-media p{color:#fff;text-shadow:0 2px 12px rgba(0,0,0,.6)}
        .cw-layout-split .pf-v5-c-login,.cw-layout-slide .pf-v5-c-login,.cw-layout-poster .pf-v5-c-login{margin-left:50vw}
        .cw-side-right.cw-layout-split .pf-v5-c-login,.cw-side-right.cw-layout-slide .pf-v5-c-login,.cw-side-right.cw-layout-poster .pf-v5-c-login{margin-left:0;margin-right:50vw}
        .cw-layout-spotlight .pf-v5-c-login{display:block;min-height:100vh}
        .cw-layout-spotlight .pf-v5-c-login__container{position:fixed;top:24px;bottom:24px;right:24px;left:auto;width:min(480px,calc(100vw - 48px));margin:0;overflow:auto;
        display:flex;flex-direction:column;justify-content:center;padding:3rem 2.5rem}
        .cw-side-left.cw-layout-spotlight .pf-v5-c-login__container{right:auto;left:24px}
        .cw-caption{position:fixed;left:3rem;bottom:3rem;max-width:11ch;padding:0;font-size:clamp(2.75rem,6vw,6.5rem)}
        .cw-side-left .cw-caption{left:auto;right:3rem;text-align:right}
        }
        @media (max-width:767px){
        .cw-layout-spotlight .pf-v5-c-login__container{border-radius:1.75rem 1.75rem 0 0;min-height:70vh}
        }
        @media (prefers-reduced-motion:reduce){.cw-aurora span,.cw-background,.cw-media,.cw-layout-stack .pf-v5-c-login__main::before{animation:none!important}}

        """;

    private const string StaticScript = """
        (function () {
          var root = document.documentElement;
          root.classList.add("cw-layout-" + coworkee.layout, "cw-side-" + coworkee.side);
          if (coworkee.glass) {
            root.classList.add("cw-surface-glass", "cw-transparent");
          }

          if (coworkee.aurora) {
            root.classList.add("cw-with-aurora", "cw-transparent");
          }

          if (coworkee.logoSize) {
            root.classList.add("cw-logo-" + coworkee.logoSize);
          }

          if (coworkee.layout === "spotlight") {
            root.classList.add("cw-transparent");
          }

          if (coworkee.panelAnimated) {
            root.classList.add("cw-panel-animated");
          }

          if (coworkee.background || coworkee.fill) {
            // behind the whole page, independent of the panel's media: an image, a video or the page fill (--cw-page-fill)
            root.classList.add("cw-has-background");
            if (coworkee.fillAnimated) {
              root.classList.add("cw-fill-animated");
            }

            document.addEventListener("DOMContentLoaded", function () {
              var back = document.createElement("div");
              back.className = "cw-background";
              back.setAttribute("aria-hidden", "true");
              back.style.setProperty("--cw-dim", coworkee.backgroundDim + "%");
              if (coworkee.background && coworkee.backgroundVideo) {
                var clip = document.createElement("video");
                clip.src = coworkee.background;
                clip.muted = clip.loop = clip.playsInline = true;
                clip.autoplay = !matchMedia("(prefers-reduced-motion: reduce)").matches;
                back.appendChild(clip);
              } else if (coworkee.background) {
                back.style.backgroundImage = "url(" + JSON.stringify(coworkee.background) + ")";
              }

              document.body.prepend(back);
            });
          }

          // the administrators' own texts on the sign-in page, in the page's language
          var lang = (document.documentElement.lang || "").toLowerCase();
          var pick = function (texts) {
            return texts ? texts[lang] || texts[lang.split("-")[0]] || texts[""] || null : null;
          };
          if (coworkee.title || coworkee.subtitle || coworkee.notice) {
            document.addEventListener("DOMContentLoaded", function () {
              var heading = document.getElementById("kc-page-title");
              if (!heading || document.body.dataset.pageId !== "login-login") {
                return;
              }

              var title = pick(coworkee.title);
              if (title) {
                heading.textContent = title;
              }

              var after = heading;
              [["subtitle", "cw-subtitle"], ["notice", "cw-notice"]].forEach(function (part) {
                var text = pick(coworkee[part[0]]);
                if (text) {
                  var line = document.createElement("p");
                  line.className = part[1];
                  line.textContent = text;
                  after.insertAdjacentElement("afterend", line);
                  after = line;
                }
              });
            });
          }

          if (coworkee.aurora) {
            // soft fields of the fill's or the theme's colors above the page background
            document.addEventListener("DOMContentLoaded", function () {
              var aurora = document.createElement("div");
              aurora.className = "cw-aurora";
              aurora.setAttribute("aria-hidden", "true");
              for (var i = 0; i < 3; i++) {
                aurora.appendChild(document.createElement("span"));
              }

              var back = document.querySelector(".cw-background");
              if (back) {
                back.insertAdjacentElement("afterend", aurora);
              } else {
                document.body.prepend(aurora);
              }
            });
          }

          if (coworkee.layout === "spotlight" && coworkee.appName) {
            // the app name in big letters on the open side of the page
            document.addEventListener("DOMContentLoaded", function () {
              var caption = document.createElement("h2");
              caption.className = "cw-caption";
              caption.setAttribute("aria-hidden", "true");
              caption.textContent = coworkee.appName;
              document.body.prepend(caption);
            });
          }

          if (coworkee.layout === "stack") {
            // the cards behind the form show the media and lean the other way for the registration
            document.addEventListener("DOMContentLoaded", function () {
              if (document.body.dataset.pageId === "login-register") {
                root.classList.add("cw-flipped");
              }

              var main = document.querySelector(".pf-v5-c-login__main");
              if (main && coworkee.media && !coworkee.video) {
                main.style.setProperty("--cw-media", "url(" + JSON.stringify(coworkee.media) + ")");
              }
            });
          }

          if (coworkee.layout !== "split" && coworkee.layout !== "slide" && coworkee.layout !== "poster") {
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

            if (coworkee.layout !== "split" && coworkee.appName) {
              var name = document.createElement("h2");
              name.textContent = coworkee.appName;
              aside.appendChild(name);
            }

            // the poster's claim: the sign-in title
            var claim = coworkee.layout === "poster" ? pick(coworkee.title) : null;
            if (claim) {
              var line = document.createElement("p");
              line.textContent = claim;
              aside.appendChild(line);
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
