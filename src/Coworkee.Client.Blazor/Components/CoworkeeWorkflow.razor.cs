using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Workflows;
using Coworkee.Contracts.Workflows;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// The workflow of one entity for its detail page: state, the transitions the user may run, the open approval with its decisions and the history.
/// Changes of others appear live.
/// </summary>
public partial class CoworkeeWorkflow : IAsyncDisposable
{
    private WorkflowStatusDto? _status;
    private string? _topic;
    private bool _busy;
    private IAsyncDisposable? _subscription;

    [Inject] private IWorkflowApi Api { get; set; } = null!;

    [Inject] private WorkflowCommands Commands { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    [Parameter] public bool ShowHistory { get; set; } = true;

    /// <summary>After every change, e.g. to reload the entity whose fields the workflow set.</summary>
    [Parameter] public EventCallback<WorkflowStatusDto> OnChanged { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var topic = WorkflowTopics.Entity(EntityType, EntityId);
        if (topic == _topic)
        {
            return;
        }

        _topic = topic;
        _status = null;
        await UnsubscribeAsync();
        _subscription = await Realtime.SubscribeAsync(topic, _ => InvokeAsync(async () =>
        {
            await LoadAsync();
            StateHasChanged();
        }));
        await LoadAsync();
    }

    public ValueTask DisposeAsync() => UnsubscribeAsync();

    private Task<bool> LoadAsync() => Snackbar.RunAsync(async () => _status = await Api.GetAsync(EntityType, EntityId));

    private async Task RunAsync(WorkflowTransitionDto transition)
    {
        _busy = true;
        await Apply(await Commands.RunAsync(EntityType, EntityId, transition));
        _busy = false;
    }

    private async Task Apply(WorkflowStatusDto? status)
    {
        if (status is not null)
        {
            _status = status;
            await OnChanged.InvokeAsync(status);
        }
    }

    private static string DecisionIcon(ApproverDto approver) => approver.Approved switch
    {
        true => Icons.Material.Filled.CheckCircle,
        false => Icons.Material.Filled.Cancel,
        _ => Icons.Material.Outlined.HourglassEmpty,
    };

    private static Color DecisionColor(ApproverDto approver) => approver.Approved switch
    {
        true => Color.Success,
        false => Color.Error,
        _ => Color.Default,
    };

    private async ValueTask UnsubscribeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }
    }
}
