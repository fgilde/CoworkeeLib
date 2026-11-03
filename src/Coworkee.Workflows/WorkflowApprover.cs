using Coworkee.Domain;

namespace Coworkee.Workflows;

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
