using System.Text.Json;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Theming;

public sealed class ThemeService(Api.ICoworkeeApi api)
{
    private bool _dense = Density.Default.Dense;

    public CoworkeeTheme Theme { get; private set; } = CoworkeeTheme.Default;

    public ThemeDto? Current { get; private set; }

    public string Mode { get; private set; } = "system";

    public string? CustomCss => Theme.CustomCss;

    public string? Logo => Theme.Logo;

    /// <summary>The settings the server shares with the client, read once with the theme.</summary>
    public IReadOnlyDictionary<string, string?> ClientSettings { get; private set; } = new Dictionary<string, string?>();

    public event Action? Changed;

    /// <summary>Cascades the theme's <see cref="CoworkeeTheme.Dense"/> to every component (registered with AddCascadingValue).</summary>
    public CascadingValueSource<Density> DensitySource { get; } = new(Density.Default, isFixed: false);

    // both sources are read before anything changes: applying the organisation's theme first showed it for a moment before the user's choice
    public async Task LoadAsync()
    {
        ThemeDto? theme = null;
        try
        {
            theme = await api.GetCurrentThemeAsync();
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException or JsonException)
        {
        }

        try
        {
            var settings = await api.GetClientSettingsAsync() ?? new Dictionary<string, string?>();
            ClientSettings = settings;
            Mode = settings.GetValueOrDefault(ThemeSettings.Mode) is { Length: > 0 } mode ? mode : "system";
            if (settings.GetValueOrDefault(ThemeSettings.ThemeId) is { } id && Guid.TryParse(id, out var themeId) && themeId != theme?.Id)
            {
                theme = themeId == Current?.Id ? Current : (await api.GetThemesAsync() ?? []).FirstOrDefault(t => t.Id == themeId) ?? theme;
            }
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException or JsonException)
        {
        }

        if (theme is not null)
        {
            Use(theme);
        }

        Notify();
    }

    public void Apply(ThemeDto theme)
    {
        Use(theme);
        Notify();
    }

    private void Use(ThemeDto theme)
    {
        Current = theme;
        Theme = ThemeMapper.ToTheme(theme);
    }

    /// <summary>Switches between "light", "dark" and "system" for this session (setup, or a toggle before it is saved).</summary>
    public void SetMode(string mode)
    {
        Mode = mode;
        Notify();
    }

    public void Preview(CoworkeeTheme theme)
    {
        Theme = theme;
        Notify();
    }

    private void Notify()
    {
        if (Theme.Dense != _dense)
        {
            _dense = Theme.Dense;
            _ = DensitySource.NotifyChangedAsync(new Density(_dense));
        }

        Changed?.Invoke();
    }
}
