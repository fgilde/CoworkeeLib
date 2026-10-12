using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class ApiKeyStatus
{
    [Parameter, EditorRequired] public ApiKeyDto Key { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;
}
