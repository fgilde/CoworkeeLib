using Coworkee.Application.Messaging;
using Coworkee.Core.Security;
using Coworkee.Workflows.Elsa.Activities;
using Elsa.Workflows.Runtime;

namespace Coworkee.Workflows.Elsa;

/// <summary>Hands workflow transitions, decisions and entity changes to Elsa after the commit (outbox), with their data as workflow input.</summary>
internal sealed class ElsaStimuli(IStimulusSender sender, ICurrentUser currentUser)
    : IDomainEventHandler<WorkflowTransitioned>, IDomainEventHandler<ApprovalDecided>, IDomainEventHandler<EntityChanged>
{
    public Task HandleAsync(WorkflowTransitioned e, CancellationToken cancellationToken) =>
        SendAsync<WorkflowStateReached>(new StateStimulus(e.EntityType, e.To), new()
        {
            [ElsaInputs.EntityType] = e.EntityType, [ElsaInputs.EntityId] = e.EntityId, [ElsaInputs.From] = e.From, [ElsaInputs.To] = e.To,
            [ElsaInputs.Transition] = e.Transition, [ElsaInputs.Comment] = e.Comment,
        }, cancellationToken);

    public Task HandleAsync(ApprovalDecided e, CancellationToken cancellationToken) =>
        SendAsync<WaitForApproval>(new ApprovalStimulus(e.EntityType, e.EntityId), new()
        {
            [ElsaInputs.EntityType] = e.EntityType, [ElsaInputs.EntityId] = e.EntityId, [ElsaInputs.Status] = e.Status.ToString(), [ElsaInputs.Comment] = e.Comment,
        }, cancellationToken);

    public Task HandleAsync(EntityChanged e, CancellationToken cancellationToken) =>
        SendAsync<EntityChangedTrigger>(new EntityChangeStimulus(e.EntityType, e.Action), new()
        {
            [ElsaInputs.EntityType] = e.EntityType, [ElsaInputs.EntityId] = e.EntityId, [ElsaInputs.Action] = e.Action,
        }, cancellationToken);

    private async Task SendAsync<TActivity>(object stimulus, Dictionary<string, object?> input, CancellationToken cancellationToken)
        where TActivity : global::Elsa.Workflows.IActivity
    {
        input[ElsaInputs.UserId] = currentUser.UserId;
        input[ElsaInputs.TenantId] = currentUser.TenantId;
        input[ElsaInputs.ImpersonatorId] = currentUser.ImpersonatorId;
        await sender.SendAsync(global::Elsa.Workflows.Helpers.ActivityTypeNameHelper.GenerateTypeName<TActivity>(), stimulus, new StimulusMetadata { Input = input! }, cancellationToken);
    }
}
