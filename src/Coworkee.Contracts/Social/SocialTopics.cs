namespace Coworkee.Contracts.Social;

public static class SocialTopics
{
    /// <summary>Realtime topic that signals every new, changed or deleted comment of one entity.</summary>
    public static string Comments(string entityType, Guid entityId) => $"comments:{entityType}:{entityId}";

    /// <summary>Realtime topic that signals every added or taken back reaction of one entity.</summary>
    public static string Reactions(string entityType, Guid entityId) => $"reactions:{entityType}:{entityId}";
}
