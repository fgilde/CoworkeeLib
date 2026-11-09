namespace Coworkee.Workflows;

public sealed class WorkflowState<TEntity>(string name) : WorkflowState(name)
    where TEntity : class
{
    public WorkflowState<TEntity> OnEnter(Func<WorkflowContext<TEntity>, Task> action)
    {
        OnEnterActions.Add(context => action((WorkflowContext<TEntity>)context));
        return this;
    }

    /// <summary>A CSS color for the state in facets and filters, e.g. <c>#2e7d32</c> or <c>var(--mud-palette-success)</c>.</summary>
    public WorkflowState<TEntity> WithColor(string color)
    {
        Color = color;
        return this;
    }

    public WorkflowState<TEntity> OnExit(Func<WorkflowContext<TEntity>, Task> action)
    {
        OnExitActions.Add(context => action((WorkflowContext<TEntity>)context));
        return this;
    }

    /// <summary>Sets fields of the entity when the state is entered, e.g. <c>p =&gt; p.ApprovedAt = DateTimeOffset.UtcNow</c>.</summary>
    public WorkflowState<TEntity> Set(Action<TEntity> change) => OnEnter(context =>
    {
        change(context.Entity);
        return Task.CompletedTask;
    });

    /// <summary>Tells the users of a role that the entity reached this state.</summary>
    public WorkflowState<TEntity> NotifyRole(string role, string title = "{0} is now {1}", string? body = null)
    {
        NotifiedRoles.Add(role);
        return OnEnter(async context => await context.NotifyAsync(await context.UsersInRoleAsync(role), title, body));
    }

    /// <summary>
    /// Entering the state asks for an approval; once it is decided the workflow moves on to <paramref name="approved"/> or <paramref name="rejected"/>.
    /// Leaving the state by a transition cancels an open approval.
    /// </summary>
    public WorkflowState<TEntity> Approval(string approved, string rejected, Action<ApprovalStep> configure) =>
        Approval(new ApprovalStep(approved, rejected), configure);

    /// <summary>
    /// An approval whose approved target is chosen from the entity when it is decided, e.g. <c>ctx =&gt; ctx.Entity.Amount &gt; 1000 ? "Finance review" : "Approved"</c>;
    /// list every possible target with <see cref="ApprovalStep.ApprovedTo"/>.
    /// </summary>
    public WorkflowState<TEntity> Approval(Func<WorkflowContext<TEntity>, string> approved, string rejected, Action<ApprovalStep> configure) =>
        Approval(new ApprovalStep(Typed(approved), null, null, rejected), configure);

    /// <summary>An approval whose approved and rejected targets are chosen from the entity; list them with <see cref="ApprovalStep.ApprovedTo"/> and <see cref="ApprovalStep.RejectedTo"/>.</summary>
    public WorkflowState<TEntity> Approval(Func<WorkflowContext<TEntity>, string> approved, Func<WorkflowContext<TEntity>, string> rejected, Action<ApprovalStep> configure) =>
        Approval(new ApprovalStep(Typed(approved), Typed(rejected), null, null), configure);

    private static Func<WorkflowContext, string> Typed(Func<WorkflowContext<TEntity>, string> target) => context => target((WorkflowContext<TEntity>)context);

    private WorkflowState<TEntity> Approval(ApprovalStep step, Action<ApprovalStep> configure)
    {
        configure(step);
        Step = step;
        return this;
    }
}
