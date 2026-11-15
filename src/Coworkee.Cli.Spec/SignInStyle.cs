using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Coworkee.Cli.Spec;

/// <summary>Which options a sign-in layout shows and how it names its two backgrounds: Contracts' <see cref="Coworkee.Contracts.Theming.SignInLayoutOptions"/> in the spec's terms.</summary>
public sealed record SignInLayoutOptions(string PageLabel, string? PanelLabel, bool MediaSide, bool Surface, bool Animation, bool Dim, bool Tilt, bool LogoSize)
{
    /// <summary>The built-in layouts in the order the pickers show them.</summary>
    public static IReadOnlyList<string> Layouts => Coworkee.Contracts.Theming.SignInLayouts.All;

    public static SignInLayoutOptions For(string? layout)
    {
        var options = Coworkee.Contracts.Theming.SignInLayoutOptions.For(layout);
        return new(options.PageBackground ?? "Page background", options.PanelBackground, options.MediaSide is not null, options.Surface, options.Animation, options.Dim, options.Tilt, options.LogoSize);
    }

    /// <summary>The built-in layouts that show an option, for <see cref="OptionDependency"/>.</summary>
    public static string[] With(Func<SignInLayoutOptions, bool> option) => [.. Layouts.Where(l => option(For(l)))];
}

/// <summary>A background of the sign-in pages as a color or a gradient (SignInFill of the runtime): either <see cref="Color"/> or <see cref="Gradient"/>.</summary>
public sealed class SignInFillSpec
{
    [Description("A color: #rgb, #rrggbb, #rrggbbaa, rgb()/rgba()/hsl()/hsla() with numbers, or a theme color primary, secondary, tertiary, background or surface.")]
    public string? Color { get; set; }

    [Description("A gradient instead of a color.")]
    public SignInGradientSpec? Gradient { get; set; }
}

public sealed class SignInGradientSpec
{
    [Description("linear, radial or conic.")]
    public GradientKind Kind { get; set; }

    [Description("Direction of a linear gradient, start of a conic one: 0 to 359 degrees (0 points up, 90 right).")]
    public int Angle { get; set; } = 135;

    [Description("Two to eight colors along the gradient.")]
    public List<SignInGradientStopSpec> Stops { get; set; } = [];

    [Description("Shifts the gradient slowly.")]
    public bool Animated { get; set; }
}

public sealed class SignInGradientStopSpec
{
    [Description("A color like SignInFillSpec.Color.")]
    public string Color { get; set; } = string.Empty;

    [Description("Where along the gradient, 0 to 100 percent.")]
    public int Position { get; set; }
}

public enum GradientKind
{
    Linear,
    Radial,
    Conic,
}

/// <summary>The sign-in options beyond layout and media: fills, legacy layout names and the SignInDesign settings of a generated app.</summary>
public static partial class SignInStyle
{
    public const int MaxStops = Coworkee.Contracts.Theming.SignInFills.MaxStops;

    /// <summary>Theme colors a fill may name; the sign-in pages map them to the theme's variables.</summary>
    public static readonly IReadOnlyList<string> ThemeColors = Coworkee.Contracts.Theming.SignInFills.ThemeColors;

    // the runtime model's spelling: property names as they are, enums as their names
    private static readonly JsonSerializerOptions Settings = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions Compact = new(SpecJson.Options) { WriteIndented = false };

    /// <summary>Layout names of older specs that are a look of Card now: Glass is a glass surface, Aurora the drifting animation.</summary>
    public static (string Option, string Value)? Legacy(string? layout) =>
        string.Equals(layout, "Glass", StringComparison.OrdinalIgnoreCase) ? ("signInSurface", "Glass")
        : string.Equals(layout, "Aurora", StringComparison.OrdinalIgnoreCase) ? ("signInAnimation", "Aurora")
        : null;

    public static bool IsColor(string? color) => Coworkee.Contracts.Theming.SignInFills.IsColor(color);

    /// <summary>A fill as option value (a color, or the JSON of a fill); null or empty is none.</summary>
    public static SignInFillSpec? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (!value.StartsWith('{'))
        {
            return new SignInFillSpec { Color = value };
        }

        try
        {
            return JsonSerializer.Deserialize<SignInFillSpec>(value, SpecJson.Options);
        }
        catch (JsonException)
        {
            return new SignInFillSpec();
        }
    }

    /// <summary>The option value of a fill: a lone color as it is, a gradient as compact JSON of the spec.</summary>
    public static string? Format(SignInFillSpec? fill) => fill switch
    {
        null or { Color: null, Gradient: null } => null,
        { Gradient: null } => fill.Color,
        _ => JsonSerializer.Serialize(fill, Compact),
    };

    /// <summary>What is wrong with a fill option value, or null.</summary>
    public static string? Problem(string? value)
    {
        var fill = Parse(value);
        if (fill is null)
        {
            return null;
        }

        if ((fill.Color is null) == (fill.Gradient is null))
        {
            return "is neither a color nor a gradient (a color, or JSON with color or gradient)";
        }

        if (fill.Color is { } color)
        {
            return IsColor(color) ? null : "is no color: #rgb, #rrggbb, #rrggbbaa, rgb()/rgba()/hsl()/hsla() with numbers, or primary, secondary, tertiary, background, surface";
        }

        var gradient = fill.Gradient!;
        return gradient.Angle is < 0 or > 359 ? "has an angle outside 0 to 359"
            : gradient.Stops.Count is < 2 or > MaxStops ? $"needs 2 to {MaxStops} stops"
            : gradient.Stops.FirstOrDefault(s => !IsColor(s.Color)) is { } stop ? $"has a stop with the color '{stop.Color}' that is no color"
            : gradient.Stops.Any(s => s.Position is < 0 or > 100) ? "has a stop outside 0 to 100"
            : null;
    }

    /// <summary>What is wrong with a whole number option, or null.</summary>
    public static string? RangeProblem(string? value, int min, int max) =>
        value is null || (int.TryParse(value, out var number) && number >= min && number <= max) ? null : $"is not a whole number from {min} to {max}";

    /// <summary>
    /// Coworkee:Auth:Design of a generated app for the options that do not go into the app host (fills, surface, animation, tilt, logo size and darkening),
    /// spelled like SignInDesign; null when all of them keep their defaults or do not apply to the layout.
    /// </summary>
    public static JsonObject? DesignSettings(IReadOnlyDictionary<string, string?> values)
    {
        string? Value(string id) => OptionCatalog.Find(id) is { } option && OptionCatalog.IsActive(option, values) && values[id] is { } value && value != option.Default ? value : null;

        var design = new JsonObject();
        if (Value("signInBackgroundDim") is { } dim)
        {
            design["BackgroundDim"] = int.Parse(dim, System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var (id, key) in new[] { ("signInPageFill", "PageFill"), ("signInPanelFill", "PanelFill") })
        {
            if (Parse(Value(id)) is { } fill)
            {
                design[key] = JsonSerializer.SerializeToNode(fill, Settings);
            }
        }

        foreach (var (id, key) in new[] { ("signInSurface", "Surface"), ("signInAnimation", "Animation"), ("signInLogoSize", "LogoSize") })
        {
            if (Value(id) is { } value)
            {
                design[key] = value;
            }
        }

        if (Value("signInTilt") is { } tilt)
        {
            design["Tilt"] = int.Parse(tilt, System.Globalization.CultureInfo.InvariantCulture);
        }

        return design.Count == 0 ? null : design;
    }
}
