using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;

namespace Coworkee.Realtime;

internal sealed class UserTopicAuthorizer(ICurrentUser currentUser) : IRealtimeTopicAuthorizer
{
    public bool Handles(string topic) => topic.StartsWith("user:", StringComparison.Ordinal);

    public Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken) =>
        Task.FromResult(currentUser.UserId is { } id && topic == RealtimeTopics.User(id));
}
