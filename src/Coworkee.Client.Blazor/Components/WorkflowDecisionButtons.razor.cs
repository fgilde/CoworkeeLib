using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Workflows;
using Coworkee.Contracts.Workflows;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Approve and reject for an open approval; rejecting asks for an optional reason.</summary>
public partial class WorkflowDecisionButtons
{
    private bool _busy;

    [Inject] private WorkflowCommands Commands { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public Guid ApprovalId { get; set; }

    [Parameter] public EventCallback<WorkflowStatusDto?> OnDecided { get; set; }

    private async Task DecideAsync(bool approve)
    {
        _busy = true;
        var status = await Commands.DecideAsync(ApprovalId, approve);
        _busy = false;
        if (status is not null)
        {
            await OnDecided.InvokeAsync(status);
        }
    }
}
