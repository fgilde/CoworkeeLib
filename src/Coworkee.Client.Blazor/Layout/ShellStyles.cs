namespace Coworkee.Client.Blazor.Layout;

/// <summary>Rules for MudBlazor components on every screen size, rendered by the layout after the theme.</summary>
internal static class ShellStyles
{
    /// <summary>
    /// Tab badges flow inline after the tab text. MudBlazor places them absolutely beyond the text, where the tab's padding holds one digit:
    /// longer counts ran into the next tab or were clipped by the tab bar. Inline, the tab grows with its badge.
    /// </summary>
    public const string Css =
        """
        .mud-tab .mud-tab-badge .mud-badge-wrapper { position: static; width: auto; height: auto; }
        .mud-tab .mud-tab-badge .mud-badge { position: static; inset: auto; }
        """;
}
