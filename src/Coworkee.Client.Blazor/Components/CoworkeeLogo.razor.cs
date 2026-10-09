using System.Text;
using Coworkee.Client.Blazor.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The theme's logo, or the app's (<see cref="CoworkeeClientOptions.AppLogo"/>) when the theme has none.</summary>
public partial class CoworkeeLogo : IDisposable
{
    [Parameter] public int Height { get; set; } = 28;

    [Parameter] public string? Class { get; set; }

    /// <summary>Shown when neither the theme nor the app has a logo.</summary>
    [Parameter] public RenderFragment? Fallback { get; set; }

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    private string? Source => ThemeService.LogoSvg is { Length: > 0 } svg
        ? $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}"
        : Options.AppLogo;

    protected override void OnInitialized() => ThemeService.Changed += Refresh;

    public void Dispose() => ThemeService.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);
}
