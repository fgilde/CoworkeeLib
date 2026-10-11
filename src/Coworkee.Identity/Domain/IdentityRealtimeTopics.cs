using Coworkee.Contracts.Realtime;
using Coworkee.Domain;
using Coworkee.Identity.Roles;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Domain;

// ponytail: every permission-relevant change reloads the permissions of the whole organisation; per-user topics when organisations get large
internal sealed class IdentityRealtimeTopics : IRealtimeTopicMapper
{
    public IEnumerable<string> TopicsFor(object entity) => entity switch
    {
        IdentityUserRole<Guid> userRole => [RealtimeTopics.Type(nameof(User)), RealtimeTopics.Entity(nameof(User), userRole.UserId), RealtimeTopics.TenantPermissions],
        UserGroupMember member => [RealtimeTopics.Type(nameof(UserGroup)), RealtimeTopics.Entity(nameof(UserGroup), member.GroupId), RealtimeTopics.TenantPermissions],
        UserGroupRole groupRole => [RealtimeTopics.Type(nameof(UserGroup)), RealtimeTopics.Entity(nameof(UserGroup), groupRole.GroupId), RealtimeTopics.TenantPermissions],
        PermissionGrant or Role or UserGroup => [RealtimeTopics.TenantPermissions],
        IdentityUserClaim<Guid> { ClaimType: ActiveRole.ClaimType } => [RealtimeTopics.TenantPermissions],
        _ => [],
    };
}
