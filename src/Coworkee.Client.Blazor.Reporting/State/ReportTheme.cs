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
            return await module.InvokeAsync<ChartPalette>("palette");
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

public sealed record ChartPalette(bool Dark, List<string> Colors, string Text, string Lines, string Surface, string Font)
{
    public static readonly ChartPalette Fallback = new(false, ["#0f6cbd", "#7c4dff", "#00a3a3", "#2196f3", "#00c853", "#ff9800", "#f44336"], "#666", "#e0e0e0", "#fff", "Roboto, sans-serif");
}
