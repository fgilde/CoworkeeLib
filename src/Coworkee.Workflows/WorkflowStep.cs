using Coworkee.Contracts.Workflows;
using Coworkee.Domain;

namespace Coworkee.Workflows;

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
