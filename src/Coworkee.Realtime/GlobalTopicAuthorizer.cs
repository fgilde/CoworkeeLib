using Coworkee.Core.Security;

namespace Coworkee.Realtime;

/// <summary>Topics every signed-in user of every organisation may follow, e.g. "global:localization".</summary>
internal sealed class GlobalTopicAuthorizer(ICurrentUser currentUser) : IRealtimeTopicAuthorizer
{
    public const string Prefix = "global:";

    public bool Handles(string topic) => topic.StartsWith(Prefix, StringComparison.Ordinal);

    public Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken) => Task.FromResult(currentUser.UserId is not null);
}
