namespace Coworkee.Theming;

/// <summary>The default theme of the installation and the app's own themes, seeded and kept like the built-in ones.</summary>
public sealed class ThemingOptions
{
    /// <summary>Name of a built-in theme or of one of <see cref="Themes"/>; an unknown name keeps Ocean.</summary>
    public string DefaultTheme { get; set; } = ThemeSeeds.DefaultName;

    public List<AppTheme> Themes { get; } = [];
}
