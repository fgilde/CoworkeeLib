using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Workflows;
using Coworkee.Contracts.Workflows;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>State chip and a menu with the allowed transitions and the own decision, for a data table row (TemplateColumn).</summary>
public partial class CoworkeeWorkflowActions
{
    private WorkflowStatusDto? _status;
    private Guid _loaded;

    [Inject] private IWorkflowApi Api { get; set; } = null!;

    [Inject] private WorkflowCommands Commands { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    /// <summary>After every change, e.g. to reload the table when the workflow mirrors its state into a column.</summary>
    [Parameter] public EventCallback<WorkflowStatusDto> OnChanged { get; set; }

    // ponytail: one request per row; a batch endpoint once tables show hundreds of rows at a time
    protected override async Task OnParametersSetAsync()
    {
        if (_loaded != EntityId)
        {
            _loaded = EntityId;
            await Snackbar.RunAsync(async () => _status = await Api.GetAsync(EntityType, EntityId));
        }
    }

    private async Task RunAsync(WorkflowTransitionDto transition) => await Apply(await Commands.RunAsync(EntityType, EntityId, transition));

    private async Task DecideAsync(Guid approvalId, bool approve) => await Apply(await Commands.DecideAsync(approvalId, approve));

    private async Task Apply(WorkflowStatusDto? status)
    {
        if (status is not null)
        {
            _status = status;
            await OnChanged.InvokeAsync(status);
        }
    }
}
