using System.Text.Json;
using Coworkee.Contracts.Theming;

namespace Coworkee.Client.Blazor.Theming;

public sealed class ThemeService(Api.ICoworkeeApi api)
{
    public CoworkeeTheme Theme { get; private set; } = CoworkeeTheme.Default;

    public ThemeDto? Current { get; private set; }

    public string Mode { get; private set; } = "system";

    public string? CustomCss => Theme.CustomCss;

    public string? LogoSvg => Theme.LogoSvg;

    /// <summary>The settings the server shares with the client, read once with the theme.</summary>
    public IReadOnlyDictionary<string, string?> ClientSettings { get; private set; } = new Dictionary<string, string?>();

    public event Action? Changed;

    public async Task LoadAsync()
    {
        try
        {
            if (await api.GetCurrentThemeAsync() is { } current)
            {
                Apply(current);
            }
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException or JsonException)
        {
        }

        try
        {
            var settings = await api.GetClientSettingsAsync() ?? new Dictionary<string, string?>();
            ClientSettings = settings;
            Mode = settings.GetValueOrDefault(ThemeSettings.Mode) is { Length: > 0 } mode ? mode : "system";
            if (settings.GetValueOrDefault(ThemeSettings.ThemeId) is { } id && Guid.TryParse(id, out var themeId) && themeId != Current?.Id
                && (await api.GetThemesAsync() ?? []).FirstOrDefault(t => t.Id == themeId) is { } chosen)
            {
                Apply(chosen);
            }
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException or JsonException)
        {
        }

        Changed?.Invoke();
    }

    public void Apply(ThemeDto theme)
    {
        Current = theme;
        Theme = ThemeMapper.ToTheme(theme);
        Changed?.Invoke();
    }

    /// <summary>Switches between "light", "dark" and "system" for this session (setup, or a toggle before it is saved).</summary>
    public void SetMode(string mode)
    {
        Mode = mode;
        Changed?.Invoke();
    }

    public void Preview(CoworkeeTheme theme)
    {
        Theme = theme;
        Changed?.Invoke();
    }
}
