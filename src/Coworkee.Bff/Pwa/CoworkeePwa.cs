using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Bff.Pwa;

/// <summary>
/// Makes the app installable: links the web manifest and sets the browser's title bar to the app bar colour of the visitor's theme.
/// Render it into App.razor's head; the app registers the service worker only on pages that link a manifest.
/// </summary>
public sealed class CoworkeePwa : ComponentBase
{
    private string? _light;
    private string? _dark;

    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    [Inject] private LoadingScreenSource Source { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        if (HttpContext is not null)
        {
            var theme = (await Source.GetAsync(HttpContext, LoadingScreenPreferences.Read(HttpContext.Request).ThemeId))?.Theme;
            (_light, _dark) = (PwaEndpoints.BarColor(theme?.PaletteLight), PwaEndpoints.BarColor(theme?.PaletteDark));
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var html = $"""
            <link rel="manifest" href="{PwaEndpoints.ManifestPath}" crossorigin="use-credentials" />
            <link rel="apple-touch-icon" href="{PwaEndpoints.IconPath}" />
            <meta name="mobile-web-app-capable" content="yes" />
            <meta name="apple-mobile-web-app-capable" content="yes" />
            """;
        if (_light is not null)
        {
            html += $"""<meta name="theme-color" media="(prefers-color-scheme: light)" content="{WebUtility.HtmlEncode(_light)}" />""";
        }

        if (_dark is not null)
        {
            html += $"""<meta name="theme-color" media="(prefers-color-scheme: dark)" content="{WebUtility.HtmlEncode(_dark)}" />""";
        }

        builder.AddMarkupContent(0, html);
    }
}
