using System.Reflection;
using System.Text.Json;
using Coworkee.Contracts.Theming;
using MudBlazor;
using MudBlazor.Utilities;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>
/// Maps the stored theme sections (palettes, typography, layout, shadows, options) to a <see cref="CoworkeeTheme"/> and back. The options hold the
/// properties of <see cref="CoworkeeTheme"/> and of an app's class derived from it.
/// </summary>
public static class ThemeMapper
{
    private static readonly string[] Meta = [nameof(CoworkeeTheme.IsPublished), nameof(CoworkeeTheme.Logo), nameof(CoworkeeTheme.CustomCss)];

    public static CoworkeeTheme ToTheme(ThemeDto dto) => ToTheme(dto, typeof(CoworkeeTheme));

    /// <summary>The theme as instance of <paramref name="type"/>, the app's theme class (AddCoworkeeTheme).</summary>
    public static CoworkeeTheme ToTheme(ThemeDto dto, Type type)
    {
        var theme = Create(type);
        theme.IsPublished = dto.IsPublished;
        theme.Logo = dto.Logo;
        theme.CustomCss = dto.CustomCss;
        Fill(theme.PaletteLight, dto.PaletteLight);
        Fill(theme.PaletteDark, dto.PaletteDark);
        Fill(theme.Typography, dto.Typography);
        Fill(theme.LayoutProperties, dto.LayoutProperties);
        Fill(theme.Shadows, dto.Shadows);
        Fill(theme, dto.Options, Options);
        if (dto.Options is { ValueKind: JsonValueKind.Object } options && !options.TryGetProperty(nameof(CoworkeeTheme.Dense), out _)
            && options.TryGetProperty(LegacyDense, out var legacy) && legacy.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            theme.Dense = legacy.GetBoolean();
        }

        return theme;
    }

    /// <summary>A new theme of the app's theme class with its own defaults (MudBlazor's colors, unlike <see cref="CoworkeeTheme.Default"/>).</summary>
    internal static CoworkeeTheme Create(Type type) => (CoworkeeTheme)Activator.CreateInstance(type)!;

    /// <summary><paramref name="theme"/> as instance of <paramref name="type"/>, with the values it shares.</summary>
    internal static CoworkeeTheme As(CoworkeeTheme theme, Type type)
    {
        if (theme.GetType() == type)
        {
            return theme;
        }

        var r = ToRequest(string.Empty, theme);
        return ToTheme(new ThemeDto(Guid.Empty, r.Name, false, false, r.PaletteLight, r.PaletteDark, r.Typography, r.LayoutProperties, r.Logo, r.CustomCss, 0,
            r.Shadows, r.Options, r.IsPublished), type);
    }

    // themes saved before the table-only option became Dense; saving one again stores Dense
    private const string LegacyDense = "DenseTables";

    public static ThemeRequest ToRequest(string name, CoworkeeTheme theme) => new(
        name,
        Json(theme.PaletteLight),
        Json(theme.PaletteDark),
        Json(theme.Typography),
        Json(theme.LayoutProperties),
        string.IsNullOrWhiteSpace(theme.Logo) ? null : theme.Logo,
        string.IsNullOrWhiteSpace(theme.CustomCss) ? null : theme.CustomCss,
        Json(theme.Shadows),
        JsonSerializer.SerializeToElement(Values(theme, Options)),
        theme.IsPublished);

    // the properties CoworkeeTheme and an app's theme class add to MudTheme
    private static IEnumerable<PropertyInfo> Options(Type type) =>
        Settable(type).Where(p => p.DeclaringType != typeof(MudTheme) && typeof(CoworkeeTheme).IsAssignableFrom(p.DeclaringType) && !Meta.Contains(p.Name));

    private static IEnumerable<PropertyInfo> Settable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

    private static void Fill(object? target, JsonElement? values, Func<Type, IEnumerable<PropertyInfo>>? properties = null)
    {
        if (target is null || values is not { ValueKind: JsonValueKind.Object } json)
        {
            return;
        }

        var byName = (properties ?? Settable)(target.GetType()).ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (var value in json.EnumerateObject())
        {
            if (!byName.TryGetValue(value.Name, out var property))
            {
                continue;
            }

            try
            {
                if (value.Value.ValueKind == JsonValueKind.Object && IsSection(property.PropertyType))
                {
                    Fill(property.GetValue(target), value.Value);
                }
                else
                {
                    property.SetValue(target, Convert(property.PropertyType, value.Value));
                }
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or TargetInvocationException)
            {
            }
        }
    }

    private static object? Convert(Type type, JsonElement value) => type switch
    {
        _ when value.ValueKind == JsonValueKind.Null => null,
        _ when type == typeof(MudColor) => new MudColor(value.GetString()!),
        _ when type == typeof(string) => value.GetString(),
        _ when type == typeof(string[]) => value.EnumerateArray().Select(v => v.GetString()!).ToArray(),
        _ when type == typeof(double) => value.GetDouble(),
        _ when type == typeof(int) => value.GetInt32(),
        _ when type == typeof(bool) => value.GetBoolean(),
        _ when type.IsEnum => Enum.Parse(type, value.ToString()),
        _ when Nullable.GetUnderlyingType(type) is { } underlying => Convert(underlying, value),
        _ => throw new InvalidOperationException($"Unsupported theme property type {type.Name}."),
    };

    private static JsonElement Json(object section) => JsonSerializer.SerializeToElement(Values(section, Settable));

    private static Dictionary<string, object> Values(object source, Func<Type, IEnumerable<PropertyInfo>> properties)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in properties(source.GetType()))
        {
            object? value = property.GetValue(source) switch
            {
                MudColor color => color.ToString(MudColorOutputFormats.HexA),
                string or string[] or double or int or bool => property.GetValue(source),
                Enum e => e.ToString(),
                { } section when IsSection(property.PropertyType) => Values(section, Settable),
                _ => null,
            };
            if (value is not null)
            {
                values[property.Name] = value;
            }
        }

        return values;
    }

    private static bool IsSection(Type type) => type.IsClass && type != typeof(string) && type != typeof(MudColor) && !type.IsArray;
}
