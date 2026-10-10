namespace Coworkee.Theming;

/// <summary>The default theme of the installation and the app's own themes, seeded and kept like the built-in ones.</summary>
public sealed class ThemingOptions
{
    /// <summary>Name of a built-in theme or of one of <see cref="Themes"/>; an unknown name keeps Ocean.</summary>
    public string DefaultTheme { get; set; } = ThemeSeeds.DefaultName;

    public List<AppTheme> Themes { get; } = [];
}

/// <summary>Fails the start of a host whose app themes cannot be seeded.</summary>
internal sealed class ThemingOptionsValidation : Microsoft.Extensions.Options.IValidateOptions<ThemingOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, ThemingOptions options) =>
        ThemeSeeds.Problem(options) is { } problem ? Microsoft.Extensions.Options.ValidateOptionsResult.Fail(problem) : Microsoft.Extensions.Options.ValidateOptionsResult.Success;
}

/// <summary>
/// A built-in theme (<paramref name="BasedOn"/>) with other palette colors, e.g. <c>["Primary"] = "#0a7d5a"</c>.
/// <paramref name="Dark"/> defaults to the light colors; a changed Primary or Secondary gets a readable contrast text unless one is given.
/// </summary>
public sealed record AppTheme(string Name, string BasedOn, IReadOnlyDictionary<string, string> Light, IReadOnlyDictionary<string, string>? Dark = null);
