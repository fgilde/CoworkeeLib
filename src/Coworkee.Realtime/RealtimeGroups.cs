using Coworkee.Contracts.Realtime;

namespace Coworkee.Realtime;

public static class RealtimeGroups
{
    public static string For(Guid? tenantId, string topic) =>
        topic.StartsWith("user:", StringComparison.Ordinal) || topic.StartsWith(RealtimeTopics.SessionPrefix, StringComparison.Ordinal)
        || topic.StartsWith(GlobalTopicAuthorizer.Prefix, StringComparison.Ordinal) ? topic : $"{tenantId}|{topic}";
}
