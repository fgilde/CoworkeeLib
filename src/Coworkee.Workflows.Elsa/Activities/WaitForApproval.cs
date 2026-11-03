using Coworkee.Contracts.Workflows;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Coworkee.Workflows.Elsa.Activities;

/// <summary>Waits until the open (or next) approval of an entity is decided and continues with the outcome.</summary>
[Activity("Coworkee", "Coworkee", "Waits for the decision on an entity's approval.")]
[FlowNode(nameof(ApprovalStatus.Approved), nameof(ApprovalStatus.Rejected), nameof(ApprovalStatus.Expired))]
public sealed class WaitForApproval : Activity
{
    [Input(Description = "The entity type, e.g. Products.")]
    public Input<string> EntityType { get; set; } = default!;

    [Input(Description = "The id of the entity.")]
    public Input<Guid> EntityId { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context) =>
        context.CreateBookmark(new ApprovalStimulus(context.Get(EntityType)!, context.Get(EntityId)), ResumeAsync, includeActivityInstanceId: false);

    private static async ValueTask ResumeAsync(ActivityExecutionContext context) =>
        await context.CompleteActivityWithOutcomesAsync(ElsaValues.Text(context, ElsaInputs.Status) ?? nameof(ApprovalStatus.Expired));
}
