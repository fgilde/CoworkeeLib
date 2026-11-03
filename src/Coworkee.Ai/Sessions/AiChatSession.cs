using Coworkee.Contracts.Ai;
using Coworkee.Domain;

namespace Coworkee.Ai.Sessions;

/// <summary>A conversation of a user with the assistant; it follows the user to every tab and device.</summary>
[NotAudited]
public sealed class AiChatSession : Entity, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<AiChatMessage> Messages { get; set; } = [];

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];

    public AiSessionDto ToDto() => new(Id, Title, UpdatedAt);
}
