using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Theming;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>The theme's colours for charts, read from MudBlazor's CSS variables, so custom themes and dark mode just work.</summary>
public sealed class ReportTheme : IDisposable
{
    private readonly IJSRuntime _js;
    private readonly ThemeService? _themes;
    private Task<ChartPalette>? _palette;

    public ReportTheme(IJSRuntime js, ThemeService? themes = null)
    {
        _js = js;
        _themes = themes;
        if (_themes is not null)
        {
            _themes.Changed += Reset;
        }
    }

    public event Action? Changed;

    public Task<ChartPalette> GetAsync() => _palette ??= LoadAsync();

    /// <summary>After the app bar toggled dark mode: the variables change on the next render.</summary>
    public void Reset()
    {
        _palette = null;
        _ = Task.Delay(150).ContinueWith(_ => Changed?.Invoke(), TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_themes is not null)
        {
            _themes.Changed -= Reset;
        }
    }

    private async Task<ChartPalette> LoadAsync()
    {
        try
        {
            await using var module = await _js.InvokeAsync<IJSObjectReference>("import", ReportGrid.Module);
            return await module.InvokeAsync<ChartPalette?>("palette") ?? ChartPalette.Fallback;
        }
        catch (JSException)
        {
            return ChartPalette.Fallback;
        }
        catch (InvalidOperationException)
        {
            return ChartPalette.Fallback;
        }
    }
}
