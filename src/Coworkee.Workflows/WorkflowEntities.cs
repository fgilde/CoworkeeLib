using Coworkee.Contracts.Workflows;
using Coworkee.Domain;

namespace Coworkee.Workflows;

/// <summary>The state of one entity; created with the initial state the first time someone looks.</summary>
public sealed class WorkflowItem : AggregateRoot, IMultiTenant, IHasConcurrencyToken, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public required string State { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public uint Version { get; set; }

    public IEnumerable<string> RealtimeTopics => [WorkflowTopics.Entity(EntityType, EntityId)];

    internal void Publish(IDomainEvent domainEvent) => Raise(domainEvent);
}

/// <summary>One line of the history: a transition, a decision, a reminder, an escalation or an expiry.</summary>
[NotAudited]
public sealed class WorkflowStep : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public WorkflowStepKind Kind { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public string? Transition { get; set; }

    public Guid? UserId { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset At { get; set; }
}

/// <summary>An approval of one state; changes reach the inboxes of its approvers through their user topics.</summary>
public sealed class WorkflowApproval : Entity, IMultiTenant, IHasConcurrencyToken, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>The state that asked for the approval.</summary>
    public required string State { get; set; }

    public required string Title { get; set; }

    public string? Link { get; set; }

    public bool RequiresAll { get; set; }

    public ApprovalStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset? RemindAt { get; set; }

    public DateTimeOffset? RemindedAt { get; set; }

    public DateTimeOffset? EscalatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public uint Version { get; set; }

    public List<WorkflowApprover> Approvers { get; } = [];

    public IEnumerable<string> RealtimeTopics => Approvers.Select(a => Contracts.Realtime.RealtimeTopics.User(a.UserId));
}

/// <summary>An approver of an approval with the decision once made; the change reaches the approver's inbox through the user topic.</summary>
public sealed class WorkflowApprover : Entity, IHasRealtimeTopics
{
    public Guid ApprovalId { get; set; }

    public Guid UserId { get; set; }

    public bool? Approved { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Joined at the deadline; decides alone.</summary>
    public bool Escalation { get; set; }

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];
}

/// <summary>The entity moved; handlers (IDomainEventHandler) run after the commit through the outbox.</summary>
public sealed record WorkflowTransitioned(string EntityType, Guid EntityId, string From, string To, string? Transition, Guid? UserId, string? Comment) : IDomainEvent;

public sealed record ApprovalStarted(Guid ApprovalId, string EntityType, Guid EntityId, string State, IReadOnlyList<Guid> Approvers) : IDomainEvent;

public sealed record ApprovalDecided(Guid ApprovalId, string EntityType, Guid EntityId, ApprovalStatus Status, Guid? UserId, string? Comment) : IDomainEvent;
