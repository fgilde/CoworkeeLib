using Coworkee.Core.Security;

namespace Coworkee.Realtime;

/// <summary>Topics every signed-in user follows within the own organisation, e.g. "tenant:users".</summary>
internal sealed class TenantTopicAuthorizer(ICurrentUser currentUser) : IRealtimeTopicAuthorizer
{
    public bool Handles(string topic) => topic.StartsWith("tenant:", StringComparison.Ordinal);

    public Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken) => Task.FromResult(currentUser.UserId is not null && currentUser.TenantId is not null);
}
