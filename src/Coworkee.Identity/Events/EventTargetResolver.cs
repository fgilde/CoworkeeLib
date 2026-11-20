using Coworkee.Application.Events;

namespace Coworkee.Identity.Events;

internal sealed class EventTargetResolver : IEventTargetResolver
{
    public Task<IReadOnlyCollection<Guid>> UsersAsync(EventTarget target, IReadOnlyCollection<Guid>? among = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyCollection<string>> TopicsAsync(EventTarget target, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
