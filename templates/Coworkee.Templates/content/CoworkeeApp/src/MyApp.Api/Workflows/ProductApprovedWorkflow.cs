using Coworkee.Workflows.Elsa.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;

namespace MyApp.Api.Workflows;

/// <summary>An Elsa workflow in code: when a product is approved, the product managers hear about it. Build more in Elsa Studio.</summary>
public sealed class ProductApprovedWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder) => builder.Root = new Sequence
    {
        Activities =
        {
            new WorkflowStateReached { EntityType = new("Products"), State = new("Approved"), CanStartWorkflow = true },
            new Notify { Role = new("Product Manager"), Title = new("A product was approved"), Link = new("/catalog/products") },
        },
    };
}
