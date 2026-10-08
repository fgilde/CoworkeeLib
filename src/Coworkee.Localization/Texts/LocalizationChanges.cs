using Coworkee.Contracts.Localization;
using Coworkee.Realtime;

namespace Coworkee.Localization.Texts;

/// <summary>After languages or texts changed: drops the cached texts and tells every client to reload.</summary>
public sealed class LocalizationChanges(TextStore store, IRealtimePublisher realtime)
{
    public async Task NotifyAsync(CancellationToken cancellationToken)
    {
        await store.InvalidateAsync(cancellationToken);
        await realtime.PublishAsync(null, LocalizationEvents.Topic, LocalizationEvents.Changed, new { }, cancellationToken);
    }
}
