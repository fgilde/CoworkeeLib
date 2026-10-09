using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Renders the sections of <see cref="CoworkeeClientOptions.About"/>; replace it with ReplaceComponent for a completely different dialog.</summary>
public partial class AboutDialog
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;
}
