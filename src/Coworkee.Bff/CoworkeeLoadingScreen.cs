using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Bff;

/// <summary>
/// The app's loading screen, rendered by the server into App.razor's body (static, before the WebAssembly app runs) from
/// <see cref="LoadingScreenSource"/>; the app's layout hides it once the theme is applied.
/// </summary>
public sealed class CoworkeeLoadingScreen : ComponentBase
{
    private string? _markup;

    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    [Inject] private LoadingScreenSource Source { get; set; } = null!;

    /// <summary>Shown when the theme has no logo, e.g. the app's icon.</summary>
    [Parameter] public string? Logo { get; set; }

    // shortcut: read by screen readers only and the server knows no language before the app runs, so it stays English
    [Parameter] public string Label { get; set; } = "Loading";

    protected override async Task OnInitializedAsync()
    {
        if (HttpContext is not null)
        {
            _markup = LoadingScreenMarkup.Render(await Source.GetAsync(HttpContext), HttpContext.Request.Cookies[LoadingScreenMarkup.ModeCookie], Logo, Label);
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
