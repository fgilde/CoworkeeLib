using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Auditing;

public partial class LogBlock
{
    [Parameter] public string? Text { get; set; }

    [Parameter] public bool Hidden { get; set; }

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;
}
