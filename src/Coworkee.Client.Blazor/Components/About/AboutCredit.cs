namespace Coworkee.Client.Blazor.Components.About;

/// <summary>
/// A library the app is built with; <paramref name="Icon"/> is an image address or a MudBlazor icon, <paramref name="IconBackground"/> a CSS
/// background behind it (for light logos).
/// </summary>
public sealed record AboutCredit(string Title, string Url, string Icon, string? Version = null, string? IconBackground = null)
{
    /// <summary>The AirWorks web desktop, for apps with an AirWorks client.</summary>
    public static AboutCredit AirWorks(string? version = null) =>
        new("AirWorks", "https://fgilde.github.io/AirWorks/", "https://fgilde.github.io/AirWorks/icons/logo-air.png", version, "#35496b");
}
