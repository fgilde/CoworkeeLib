using Coworkee.Application.Authorization;
using Coworkee.Contracts.Social;
using Coworkee.Realtime;

namespace Coworkee.Social;

/// <summary>Lets people who may read the comments (or reactions) of an entity follow "comments:{entityType}:{entityId}" (or "reactions:...").</summary>
internal sealed class SocialTopicAuthorizer(SocialGuard guard, IPermissionChecker permissions, SocialFeature feature) : IRealtimeTopicAuthorizer
{
    private string Prefix => feature == SocialFeature.Comments ? "comments:" : "reactions:";

    public bool Handles(string topic) => topic.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken)
    {
        var separator = topic.LastIndexOf(':');
        if (separator <= Prefix.Length || !Guid.TryParse(topic[(separator + 1)..], out var entityId))
        {
            return false;
        }

        var entityType = topic[Prefix.Length..separator];
        var (canonical, view) = feature == SocialFeature.Comments
            ? (SocialTopics.Comments(entityType, entityId), SocialPermissions.Comments.View)
            : (SocialTopics.Reactions(entityType, entityId), SocialPermissions.Reactions.View);
        return canonical == topic
            && await permissions.IsGrantedAsync(view, cancellationToken)
            && await guard.AllowsAsync(new SocialAccess(feature, entityType, entityId, Write: false), cancellationToken);
    }
}
