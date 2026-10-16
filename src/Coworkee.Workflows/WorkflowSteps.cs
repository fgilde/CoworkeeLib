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
    public WorkflowState<TEntity> Approval(string approved, string rejected, Action<ApprovalStep> configure)
    {
        var step = new ApprovalStep(approved, rejected);
        configure(step);
        Step = step;
        return this;
    }
}

/// <summary>Who decides and until when: one of the approvers by default, <see cref="All"/> asks every one of them; a rejection always decides.</summary>
public sealed class ApprovalStep(string approvedState, string rejectedState)
{
    public string ApprovedState { get; } = approvedState;

    public string RejectedState { get; } = rejectedState;

    public string? TimeoutState { get; private set; }

    public List<string> Roles { get; } = [];

    public List<Guid> Users { get; } = [];

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
