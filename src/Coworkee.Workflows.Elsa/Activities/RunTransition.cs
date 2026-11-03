using Coworkee.Application.Messaging;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa.Activities;

/// <summary>Runs a transition of an entity's Coworkee workflow as a user; the transition's guards apply.</summary>
[Activity("Coworkee", "Coworkee", "Runs a transition of an entity's workflow as a user, e.g. Submit.")]
public sealed class RunTransition : CodeActivity
{
    [Input(Description = "The entity type, e.g. Products.")]
    public Input<string> EntityType { get; set; } = default!;

    [Input(Description = "The id of the entity.")]
    public Input<Guid> EntityId { get; set; } = default!;

    [Input(Description = "The transition, e.g. Submit.")]
    public Input<string> Transition { get; set; } = default!;

    [Input(Description = "An optional comment for the history.")]
    public Input<string?> Comment { get; set; } = default!;

    [Input(Description = "The user to act as; empty: the user who started the workflow.")]
    public Input<Guid?> RunAs { get; set; } = default!;

    [Output(Description = "The state after the transition.")]
    public Output<string> State { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        await using var actor = await ElsaActor.BeginAsync(context, context.Get(RunAs));
        var result = await actor.Services.GetRequiredService<IDispatcher>().SendAsync(
            new RunTransitionCommand(context.Get(EntityType)!, context.Get(EntityId), context.Get(Transition)!, context.Get(Comment)), context.CancellationToken);
        context.Set(State, result.IsSuccess ? result.Value.State : throw new InvalidOperationException(result.Error.Message));
    }
}
