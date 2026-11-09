using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class SessionTable
{
    [Parameter, EditorRequired] public IReadOnlyList<UserSessionDto> Sessions { get; set; } = [];

    /// <summary>Shows an end button for every session but the caller's own.</summary>
    [Parameter] public bool CanEnd { get; set; }

    [Parameter] public EventCallback<UserSessionDto> OnEnd { get; set; }

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;
}
