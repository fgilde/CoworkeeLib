using Coworkee.Client.Blazor.Ai;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>Old links to the assistant page open the assistant window over the home page.</summary>
public partial class Assistant
{
    [Inject] private AssistantState State { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    protected override void OnInitialized()
    {
        State.Open(AssistantSize.Large);
        Nav.NavigateTo("/", replace: true);
    }
}
