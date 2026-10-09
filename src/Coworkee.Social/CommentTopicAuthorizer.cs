using Coworkee.Application.Authorization;
using Coworkee.Contracts.Social;
using Coworkee.Realtime;

namespace Coworkee.Social;

/// <summary>Lets people who may read the comments of an entity follow "comments:{entityType}:{entityId}".</summary>
internal sealed class CommentTopicAuthorizer(SocialGuard guard, IPermissionChecker permissions) : IRealtimeTopicAuthorizer
{
    private const string Prefix = "comments:";

    public bool Handles(string topic) => topic.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken)
    {
        var separator = topic.LastIndexOf(':');
        return separator > Prefix.Length
            && Guid.TryParse(topic[(separator + 1)..], out var entityId)
            && SocialTopics.Comments(topic[Prefix.Length..separator], entityId) == topic
            && await permissions.IsGrantedAsync(SocialPermissions.Comments.View, cancellationToken)
            && await guard.AllowsAsync(new SocialAccess(SocialFeature.Comments, topic[Prefix.Length..separator], entityId, Write: false), cancellationToken);
    }
}
