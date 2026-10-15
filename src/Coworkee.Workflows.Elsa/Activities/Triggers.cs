using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Coworkee.Workflows.Elsa.Activities;

/// <summary>Starts (or resumes) a workflow when an entity's Coworkee workflow reaches a state.</summary>
[Activity("Coworkee", "Coworkee", "Starts when an entity's workflow reaches a state, e.g. Products reaching Approved.", Kind = ActivityKind.Trigger)]
public sealed class WorkflowStateReached : Trigger
{
    [Input(Description = "The entity type as declared with AddCoworkeeWorkflows, e.g. Products.")]
    public Input<string> EntityType { get; set; } = default!;

    [Input(Description = "The state, e.g. Approved.")]
    public Input<string> State { get; set; } = default!;

    [Output(Description = "The id of the entity.")]
    public Output<Guid> EntityId { get; set; } = default!;

    [Output(Description = "Who ran the transition; empty when the workflow moved by itself.")]
    public Output<Guid?> UserId { get; set; } = default!;

    protected override object GetTriggerPayload(TriggerIndexingContext context) => Stimulus(context.ExpressionExecutionContext);

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        if (context.IsTriggerOfWorkflow())
        {
            await FinishAsync(context);
            return;
        }

        context.CreateBookmark(Stimulus(context.ExpressionExecutionContext), FinishAsync, includeActivityInstanceId: false);
    }

    private async ValueTask FinishAsync(ActivityExecutionContext context)
    {
        context.Set(EntityId, ElsaValues.Guid(context, ElsaInputs.EntityId) ?? Guid.Empty);
        context.Set(UserId, ElsaValues.Guid(context, ElsaInputs.UserId));
        await context.CompleteActivityAsync();
    }

    private StateStimulus Stimulus(ExpressionExecutionContext context) => new(context.Get(EntityType)!, context.Get(State)!);
}

/// <summary>Starts (or resumes) a workflow when an entity of a watched type is created, updated or deleted (AddCoworkeeElsa(e =&gt; e.Watch&lt;T&gt;(...))).</summary>
[Activity("Coworkee", "Coworkee", "Starts when an entity of a watched type is created, updated or deleted.", Kind = ActivityKind.Trigger)]
public sealed class EntityChangedTrigger : Trigger
{
    [Input(Description = "The entity type as watched with AddCoworkeeElsa, e.g. Products.")]
    public Input<string> EntityType { get; set; } = default!;

    [Input(Description = "Created, Updated or Deleted.", Options = new[] { "Created", "Updated", "Deleted" }, UIHint = "dropdown")]
    public Input<string> Action { get; set; } = new("Created");

    [Output(Description = "The id of the entity.")]
    public Output<Guid> EntityId { get; set; } = default!;

    [Output(Description = "Who made the change.")]
    public Output<Guid?> UserId { get; set; } = default!;

    protected override object GetTriggerPayload(TriggerIndexingContext context) => Stimulus(context.ExpressionExecutionContext);

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        if (context.IsTriggerOfWorkflow())
        {
            await FinishAsync(context);
            return;
        }

        context.CreateBookmark(Stimulus(context.ExpressionExecutionContext), FinishAsync, includeActivityInstanceId: false);
    }

    private async ValueTask FinishAsync(ActivityExecutionContext context)
    {
        context.Set(EntityId, ElsaValues.Guid(context, ElsaInputs.EntityId) ?? Guid.Empty);
        context.Set(UserId, ElsaValues.Guid(context, ElsaInputs.UserId));
        await context.CompleteActivityAsync();
    }

    private EntityChangeStimulus Stimulus(ExpressionExecutionContext context) => new(context.Get(EntityType)!, context.Get(Action)!);
}

internal static class ElsaValues
{
    /// <summary>A Guid from the workflow input, which arrives as a Guid, a string or a JSON value depending on how it was stored.</summary>
    public static Guid? Guid(ActivityExecutionContext context, string key) =>
        context.WorkflowInput.TryGetValue(key, out var value) && System.Guid.TryParse(value?.ToString(), out var id) ? id : null;

    public static string? Text(ActivityExecutionContext context, string key) =>
        context.WorkflowInput.TryGetValue(key, out var value) ? value?.ToString() : null;
}
