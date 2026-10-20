using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>The icon and label of the band a value falls into.</summary>
public partial class ThresholdBadge
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public ThresholdStop? Stop { get; set; }

    [Parameter] public bool ShowLabel { get; set; }

    [Parameter] public string? Class { get; set; }
}
