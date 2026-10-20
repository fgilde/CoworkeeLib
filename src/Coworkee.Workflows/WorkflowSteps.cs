namespace Coworkee.Workflows;

public class WorkflowTransition(string name, IReadOnlyList<string> from, string to)
{
    public string Name { get; } = name;

    public IReadOnlyList<string> From { get; } = from;

    public string To { get; } = to;

    internal string? Permission { get; set; }

    internal List<string> Roles { get; } = [];

    internal List<Func<WorkflowContext, Task<bool>>> Guards { get; } = [];

    internal List<Func<WorkflowContext, Task>> Actions { get; } = [];

    internal bool CommentRequired { get; set; }
}

/// <summary>A transition users run; every guard has to pass: the permission, one of the roles and every condition.</summary>
public sealed class WorkflowTransition<TEntity>(string name, IReadOnlyList<string> from, string to) : WorkflowTransition(name, from, to)
    where TEntity : class
{
    public WorkflowTransition<TEntity> RequiresPermission(string permission)
    {
        Permission = permission;
        return this;
    }

    /// <summary>The user has one of the roles (by name), directly or through a group.</summary>
    public WorkflowTransition<TEntity> RequiresRole(params string[] roles)
    {
        Roles.AddRange(roles);
        return this;
    }

    public WorkflowTransition<TEntity> When(Func<TEntity, bool> condition)
    {
        Guards.Add(context => Task.FromResult(condition((TEntity)context.Entity)));
        return this;
    }

    public WorkflowTransition<TEntity> When(Func<WorkflowContext<TEntity>, Task<bool>> condition)
    {
        Guards.Add(context => condition((WorkflowContext<TEntity>)context));
        return this;
    }

    public WorkflowTransition<TEntity> RequiresComment()
    {
        CommentRequired = true;
        return this;
    }

    /// <summary>Runs after the state changed, before the enter actions of the new state.</summary>
    public WorkflowTransition<TEntity> Then(Func<WorkflowContext<TEntity>, Task> action)
    {
        Actions.Add(context => action((WorkflowContext<TEntity>)context));
        return this;
    }
}

public class WorkflowState(string name)
{
    public string Name { get; } = name;

    /// <summary>A CSS color for the state in facets and filters, e.g. <c>#2e7d32</c> or <c>var(--mud-palette-success)</c>.</summary>
    public string? Color { get; internal set; }

    internal List<Func<WorkflowContext, Task>> OnEnterActions { get; } = [];

    internal List<Func<WorkflowContext, Task>> OnExitActions { get; } = [];

    internal ApprovalStep? Step { get; set; }
}

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
    public WorkflowState<TEntity> NotifyRole(string role, string title = "{0} is now {1}", string? body = null) =>
        OnEnter(async context => await context.NotifyAsync(await context.UsersInRoleAsync(role), title, body));

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

/// <summary>Who decides and until when: one of the approvers by default, <see cref="All"/> asks every one of them; a rejection always decides.</summary>
public sealed class ApprovalStep
{
    private readonly Func<WorkflowContext, string>? _approvedOf;

    private readonly Func<WorkflowContext, string>? _rejectedOf;

    private readonly List<string> _approvedTargets = [];

    private readonly List<string> _rejectedTargets = [];

    public ApprovalStep(string approvedState, string rejectedState)
        : this(null, null, approvedState, rejectedState)
    {
    }

    internal ApprovalStep(Func<WorkflowContext, string>? approvedOf, Func<WorkflowContext, string>? rejectedOf, string? approvedState, string? rejectedState)
    {
        _approvedOf = approvedOf;
        _rejectedOf = rejectedOf;
        ApprovedState = approvedState;
        RejectedState = rejectedState;
    }

    /// <summary>The fixed target after an approval; null when it is chosen from the entity.</summary>
    public string? ApprovedState { get; }

    /// <summary>The fixed target after a rejection; null when it is chosen from the entity.</summary>
    public string? RejectedState { get; }

    /// <summary>Every state an approval may move to.</summary>
    public IReadOnlyList<string> ApprovedTargets => ApprovedState is { } state ? [state] : _approvedTargets;

    /// <summary>Every state a rejection may move to.</summary>
    public IReadOnlyList<string> RejectedTargets => RejectedState is { } state ? [state] : _rejectedTargets;

    /// <summary>The states the approved target chosen from the entity may be; checked at start-up and at every decision.</summary>
    public ApprovalStep ApprovedTo(params string[] states)
    {
        _approvedTargets.AddRange(states);
        return this;
    }

    /// <summary>The states the rejected target chosen from the entity may be.</summary>
    public ApprovalStep RejectedTo(params string[] states)
    {
        _rejectedTargets.AddRange(states);
        return this;
    }

    internal string TargetOf(bool approved, WorkflowContext context)
    {
        var (fixedState, choose, targets) = approved ? (ApprovedState, _approvedOf, ApprovedTargets) : (RejectedState, _rejectedOf, RejectedTargets);
        var target = fixedState ?? choose!(context);
        return targets.Contains(target, StringComparer.Ordinal)
            ? target
            : throw new InvalidOperationException($"The approval of {context.EntityType} chose \"{target}\", which is not one of {string.Join(", ", targets)}.");
    }

    internal void Validate(string entityType, string state)
    {
        if (ApprovedTargets.Count == 0 || RejectedTargets.Count == 0)
        {
            throw new InvalidOperationException(
                $"The approval of the state \"{state}\" in the workflow of {entityType} chooses its target from the entity; list the possible states with ApprovedTo(...) or RejectedTo(...).");
        }
    }

    public string? TimeoutState { get; private set; }

    public List<string> Roles { get; } = [];

    public List<Guid> Users { get; } = [];

    public List<Func<WorkflowContext, Task<IReadOnlyList<Guid>>>> UserResolvers { get; } = [];

    public bool RequiresAll { get; private set; }

    public bool FourEyes { get; private set; }

    public TimeSpan? Deadline { get; private set; }

    public TimeSpan? Reminder { get; private set; }

    public List<string> EscalationRoles { get; } = [];

    /// <summary>The members of the role at the moment the approval starts.</summary>
    public ApprovalStep Role(params string[] roles)
    {
        Roles.AddRange(roles);
        return this;
    }

    public ApprovalStep User(params Guid[] users)
    {
        Users.AddRange(users);
        return this;
    }

    /// <summary>Approvers found when the approval starts, e.g. the manager of the requester; together with <see cref="Role"/> and <see cref="User"/>.</summary>
    public ApprovalStep UsersFrom(Func<WorkflowContext, Task<IReadOnlyList<Guid>>> resolve)
    {
        UserResolvers.Add(resolve);
        return this;
    }

    public ApprovalStep All()
    {
        RequiresAll = true;
        return this;
    }

    /// <summary>The user whose transition started the approval is not one of its approvers.</summary>
    public ApprovalStep NotByRequester()
    {
        FourEyes = true;
        return this;
    }

    public ApprovalStep DueIn(TimeSpan deadline)
    {
        Deadline = deadline;
        return this;
    }

    /// <summary>Reminds the approvers who have not decided yet, once, this long after the start.</summary>
    public ApprovalStep RemindAfter(TimeSpan reminder)
    {
        Reminder = reminder;
        return this;
    }

    /// <summary>At the deadline the members of the role join as approvers; one of them decides alone.</summary>
    public ApprovalStep EscalateTo(params string[] roles)
    {
        EscalationRoles.AddRange(roles);
        return this;
    }

    /// <summary>At the deadline the approval expires and the workflow moves to <paramref name="state"/>; wins over <see cref="EscalateTo"/>.</summary>
    public ApprovalStep OnTimeout(string state)
    {
        TimeoutState = state;
        return this;
    }
}
