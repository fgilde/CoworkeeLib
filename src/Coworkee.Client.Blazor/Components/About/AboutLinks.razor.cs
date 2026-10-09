using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.About;

/// <summary>The app's own links from <see cref="CoworkeeClientOptions.AboutLinks"/>.</summary>
public partial class AboutLinks
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;
}
