using System.Text.Json;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Theming;

public sealed class ThemeService(Api.ICoworkeeApi api, AppThemeType? themeType = null)
{
    private bool _dense = Density.Default.Dense;

    /// <summary>The app's theme class (AddCoworkeeTheme), else <see cref="CoworkeeTheme"/>; <see cref="Theme"/> is always one.</summary>
    public Type ThemeType { get; } = themeType?.Type ?? typeof(CoworkeeTheme);

    public CoworkeeTheme Theme { get; private set; } = ThemeMapper.As(CoworkeeTheme.Default, themeType?.Type ?? typeof(CoworkeeTheme));

    public ThemeDto? Current { get; private set; }

    public string Mode { get; private set; } = "system";

    public string? CustomCss => Theme.CustomCss;

    public string? Logo => Theme.Logo;

    /// <summary>The settings the server shares with the client, read once with the theme.</summary>
    public IReadOnlyDictionary<string, string?> ClientSettings { get; private set; } = new Dictionary<string, string?>();

    public event Action? Changed;

    private Task<IReadOnlyList<ThemeDto>>? _themes;

    /// <summary>
    /// The themes the user may choose, read once per <see cref="LoadAsync"/>: lists that read them again on <see cref="Changed"/> show a theme
    /// deleted, added or edited elsewhere at once.
    /// </summary>
    public Task<IReadOnlyList<ThemeDto>> ThemesAsync() => _themes ??= FetchThemesAsync();

    private async Task<IReadOnlyList<ThemeDto>> FetchThemesAsync()
    {
        try
        {
            return await api.GetThemesAsync() ?? [];
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Cascades the theme's <see cref="CoworkeeTheme.Dense"/> to every component (registered with AddCascadingValue).</summary>
    public CascadingValueSource<Density> DensitySource { get; } = new(Density.Default, isFixed: false);

    // both sources are read before anything changes: applying the organisation's theme first showed it for a moment before the user's choice
    public async Task LoadAsync()
    {
        _themes = null;
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
                // read again every time: the chosen theme may have been edited since, or deleted (then the organisation's theme applies)
                theme = (await ThemesAsync()).FirstOrDefault(t => t.Id == themeId) ?? theme;
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
        Theme = ThemeMapper.ToTheme(theme, ThemeType);
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

/// <summary>The app's theme class, registered by AddCoworkeeTheme.</summary>
public sealed record AppThemeType(Type Type);
