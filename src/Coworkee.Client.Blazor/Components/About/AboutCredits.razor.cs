using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.About;

/// <summary>The libraries of <see cref="AboutOptions.Credits"/> with their logo and the version the app runs.</summary>
public partial class AboutCredits
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private static bool IsImage(string icon) => !icon.TrimStart().StartsWith('<');

    private static string? IconStyle(AboutCredit credit) =>
        credit.IconBackground is { Length: > 0 } background ? $"background: {background}; border-radius: 10px; padding: 6px" : null;
}
