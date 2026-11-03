namespace Coworkee.Workflows;

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
