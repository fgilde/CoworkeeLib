using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Workflows;
using MudBlazor;

namespace Coworkee.Client.Blazor.Workflows;

/// <summary>Runs a transition or a decision from any component: asks for the comment where one is needed, shows errors; null when nothing happened.</summary>
public sealed class WorkflowCommands(IWorkflowApi api, IDialogService dialogs, ISnackbar snackbar, CoworkeeLocalizer L)
{
    public async Task<WorkflowStatusDto?> RunAsync(string entityType, Guid entityId, WorkflowTransitionDto transition)
    {
        var comment = transition.CommentRequired ? await AskAsync(L[transition.Name], required: true) : string.Empty;
        return comment is null ? null : await SendAsync(() => api.RunAsync(entityType, entityId, transition.Name, Blank(comment)));
    }

    /// <summary>Approving goes straight; rejecting asks for an optional reason first.</summary>
    public async Task<WorkflowStatusDto?> DecideAsync(Guid approvalId, bool approve)
    {
        var comment = approve ? string.Empty : await AskAsync(L["Reject"], required: false);
        return comment is null ? null : await SendAsync(() => api.DecideAsync(approvalId, approve, Blank(comment)));
    }

    private async Task<string?> AskAsync(string title, bool required)
    {
        var dialog = await dialogs.ShowAsync<WorkflowCommentDialog>(title, new DialogParameters { [nameof(WorkflowCommentDialog.Required)] = required }, EditDialogExtensions.Small());
        return await dialog.Result is { Canceled: false, Data: string text } ? text : null;
    }

    private async Task<WorkflowStatusDto?> SendAsync(Func<Task<WorkflowStatusDto>> send)
    {
        WorkflowStatusDto? status = null;
        await snackbar.RunAsync(async () => status = await send());
        return status;
    }

    private static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
