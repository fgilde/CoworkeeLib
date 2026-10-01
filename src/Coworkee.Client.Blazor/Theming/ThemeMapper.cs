using System.Reflection;
using System.Text.Json;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Theming;
using MudBlazor;
using MudBlazor.Utilities;

namespace Coworkee.Client.Blazor.Theming;

public static class ThemeMapper
{
    public static MudTheme ToMudTheme(ThemeDto dto)
    {
        var theme = new MudTheme();
        Fill(theme.PaletteLight, dto.PaletteLight);
        Fill(theme.PaletteDark, dto.PaletteDark);
        if (dto.LayoutProperties is { } layout)
        {
            Fill(theme.LayoutProperties, layout);
        }

        return theme;
    }

    public static ThemeRequest ToRequest(string name, MudTheme theme, string? logoSvg, string? customCss) => new(
        name,
        ToJson(theme.PaletteLight),
        ToJson(theme.PaletteDark),
        null,
        ToJson(theme.LayoutProperties),
        logoSvg,
        customCss);

    private static void Fill(object target, JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var properties = Properties(target);
        foreach (var value in values.EnumerateObject())
        {
            if (!properties.TryGetValue(value.Name, out var property))
            {
                continue;
            }

            try
            {
                property.SetValue(target, Convert(property.PropertyType, value.Value));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or TargetInvocationException)
            {
            }
        }
    }

    private static object? Convert(Type type, JsonElement value) => type switch
    {
        _ when type == typeof(MudColor) => new MudColor(value.GetString()!),
        _ when type == typeof(double) => value.GetDouble(),
        _ when type == typeof(int) => value.GetInt32(),
        _ when type == typeof(bool) => value.GetBoolean(),
        _ when type == typeof(string) => value.GetString(),
        _ => throw new InvalidOperationException($"Unsupported theme property type {type.Name}."),
    };

    private static JsonElement ToJson(object source)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in Properties(source).Values)
        {
            var value = property.GetValue(source);
            values[property.Name] = value switch
            {
                MudColor color => color.ToString(MudColorOutputFormats.HexA),
                double or int or bool or string => value,
                _ => null,
            };
        }

        return JsonSerializer.SerializeToElement(values.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value));
    }

    private static Dictionary<string, PropertyInfo> Properties(object target) =>
        target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                && (p.PropertyType == typeof(MudColor) || p.PropertyType == typeof(double) || p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string)))
            .ToDictionary(p => p.Name, StringComparer.Ordinal);
}

public sealed class ThemeService(Api.ICoworkeeApi api)
{
    public MudTheme Theme { get; private set; } = CoworkeeTheme.Default;

    public ThemeDto? Current { get; private set; }

    public string Mode { get; private set; } = "system";

    public string? CustomCss => Current?.CustomCss;

    public string? LogoSvg => Current?.LogoSvg;

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
        Theme = ThemeMapper.ToMudTheme(theme);
        Changed?.Invoke();
    }

    public void Preview(MudTheme theme)
    {
        Theme = theme;
        Changed?.Invoke();
    }
}
