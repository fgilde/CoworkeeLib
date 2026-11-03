using Coworkee.Contracts.Workflows;
using Coworkee.Domain;

namespace Coworkee.Workflows;

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

    /// <summary>Who ran the transition into the state; kept out of the approvers with NotByRequester, at the escalation too.</summary>
    public Guid? RequestedBy { get; set; }

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
