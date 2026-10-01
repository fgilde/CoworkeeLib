using Coworkee.Contracts.Realtime;
using Coworkee.Domain;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Domain;

internal sealed class IdentityRealtimeTopics : IRealtimeTopicMapper
{
    public IEnumerable<string> TopicsFor(object entity) => entity switch
    {
        IdentityUserRole<Guid> userRole => [RealtimeTopics.Type(nameof(User)), RealtimeTopics.Entity(nameof(User), userRole.UserId)],
        UserGroupMember member => [RealtimeTopics.Type(nameof(UserGroup)), RealtimeTopics.Entity(nameof(UserGroup), member.GroupId)],
        UserGroupRole groupRole => [RealtimeTopics.Type(nameof(UserGroup)), RealtimeTopics.Entity(nameof(UserGroup), groupRole.GroupId)],
        _ => [],
    };
}
