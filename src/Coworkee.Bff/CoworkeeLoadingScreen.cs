using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Bff;

/// <summary>
/// The app's loading screen, rendered by the server into App.razor's body (static, before the WebAssembly app runs) from
/// <see cref="LoadingScreenSource"/> and the visitor's <see cref="LoadingScreenPreferences"/>; the app's layout hides it once the theme is applied,
/// a failed start turns it into an error message with a reload button.
/// </summary>
public sealed class CoworkeeLoadingScreen : ComponentBase
{
    private string? _markup;

    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    [Inject] private LoadingScreenSource Source { get; set; } = null!;

    /// <summary>Shown when the theme has no logo, e.g. the app's icon.</summary>
    [Parameter] public string? Logo { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (HttpContext is not null)
        {
            // the page now carries the visitor's theme and language: no shared cache may keep it
            if (!HttpContext.Response.HasStarted)
            {
                HttpContext.Response.Headers.CacheControl = "no-store";
            }

            var preferences = LoadingScreenPreferences.Read(HttpContext.Request);
            var screen = Source.GetAsync(HttpContext, preferences.ThemeId);
            var texts = Source.TextsAsync(HttpContext, preferences.Culture);
            _markup = LoadingScreenMarkup.Render(await screen, preferences.Mode, Logo, await texts);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_markup is not null)
        {
            builder.AddMarkupContent(0, _markup);
        }
    }
}
