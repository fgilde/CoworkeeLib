using Coworkee.Contracts.Attachments;
using Coworkee.Realtime;

namespace Coworkee.Files.Attachments;

/// <summary>Lets people who may read a slot of a record follow "attachments:{entityType}:{entityId}".</summary>
internal sealed class AttachmentTopicAuthorizer(AttachmentGuard guard, RecordAttachmentOptions options) : IRealtimeTopicAuthorizer
{
    private const string Prefix = "attachments:";

    public bool Handles(string topic) => topic.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken)
    {
        var separator = topic.LastIndexOf(':');
        if (separator <= Prefix.Length || !Guid.TryParse(topic[(separator + 1)..], out var entityId))
        {
            return false;
        }

        var entityType = topic[Prefix.Length..separator];
        if (AttachmentTopics.Record(entityType, entityId) != topic)
        {
            return false;
        }

        foreach (var slot in options.Find(entityType)?.Slots.Keys ?? Enumerable.Empty<string>())
        {
            if ((await guard.CheckAsync(new(entityType, entityId, slot, false), cancellationToken)).IsSuccess)
            {
                return true;
            }
        }

        return false;
    }
}
