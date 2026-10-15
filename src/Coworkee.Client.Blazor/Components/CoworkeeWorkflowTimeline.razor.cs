using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Workflows;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The history of an entity's workflow, newest first: who moved it where, decided, and what the deadline job did.</summary>
public partial class CoworkeeWorkflowTimeline
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public IReadOnlyList<WorkflowStepDto> Steps { get; set; } = [];

    private string Text(WorkflowStepDto step) => step.Kind switch
    {
        WorkflowStepKind.Transition when step.Transition is { } name => L["{0}: {1} → {2}", L[name], L[step.From ?? string.Empty], L[step.To ?? string.Empty]],
        WorkflowStepKind.Transition => L["{0} → {1}", L[step.From ?? string.Empty], L[step.To ?? string.Empty]],
        WorkflowStepKind.Approved => L["Approved"],
        WorkflowStepKind.Rejected => L["Rejected"],
        WorkflowStepKind.Reminded => L["Reminder sent"],
        WorkflowStepKind.Escalated => L["Escalated"],
        _ => L["Expired"],
    };

    private static Color ColorOf(WorkflowStepKind kind) => kind switch
    {
        WorkflowStepKind.Approved => Color.Success,
        WorkflowStepKind.Rejected or WorkflowStepKind.Expired => Color.Error,
        WorkflowStepKind.Reminded or WorkflowStepKind.Escalated => Color.Warning,
        _ => Color.Primary,
    };
}
