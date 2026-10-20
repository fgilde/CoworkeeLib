using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Workflows;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Workflows;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>The approvals inbox: every open approval waiting for the user, oldest first; new and finished ones come and go live.</summary>
public partial class Approvals : IAsyncDisposable
{
    private IReadOnlyList<ApprovalDto>? _approvals;
    private IAsyncDisposable? _subscription;

    [Inject] private IWorkflowApi Api { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (ClientUser.From(user).TryGetUserId(out var me))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(me), envelope =>
                envelope.Payload.ValueKind == JsonValueKind.Object && EntityChangedPayload.From(envelope)?.EntityType is "WorkflowApproval" or "WorkflowApprover"
                    ? InvokeAsync(async () =>
                    {
                        await LoadAsync();
                        StateHasChanged();
                    })
                    : Task.CompletedTask);
        }

        await LoadAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private Task<bool> LoadAsync() => Snackbar.RunAsync(async () => _approvals = await Api.GetMyApprovalsAsync());
}
