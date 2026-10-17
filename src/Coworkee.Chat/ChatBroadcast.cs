using Coworkee.Contracts.Realtime;
using Coworkee.Realtime;

namespace Coworkee.Chat;

/// <summary>Sends a chat event to the own topic of each member, so every tab of every member follows.</summary>
internal sealed class ChatBroadcast(IRealtimePublisher realtime, ChatViews views)
{
    public async Task SendAsync(IEnumerable<Guid> userIds, string type, object payload, CancellationToken cancellationToken)
    {
        foreach (var userId in userIds.Distinct())
        {
            await realtime.PublishAsync(views.TenantId, RealtimeTopics.User(userId), type, payload, cancellationToken);
        }
    }

    public async Task ToMembersAsync(Guid conversationId, string type, object payload, CancellationToken cancellationToken) =>
        await SendAsync(await views.MemberIdsAsync(conversationId, cancellationToken), type, payload, cancellationToken);
}
