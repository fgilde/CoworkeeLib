using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Security;

internal sealed class SessionsApi(HttpClient http) : ApiClientBase(http), ISessionsApi
{
    private const string Identity = "api/v1/identity";

    public Task<IReadOnlyList<UserSessionDto>> GetMineAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<UserSessionDto>>($"{Identity}/sessions/me", cancellationToken);

    public Task EndAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Identity}/sessions/{id}/end", null, cancellationToken);

    public Task<UserSessionsDto> GetOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        GetAsync<UserSessionsDto>($"{Identity}/users/{userId}/sessions", cancellationToken);

    public Task EndAllAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/sessions/end", null, cancellationToken);

    public Task SetLimitAsync(Guid userId, int? maxSessions, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{userId}/session-limit", new SessionLimitRequest(maxSessions), cancellationToken);

    public Task<IReadOnlyDictionary<Guid, int>> GetCountsAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyDictionary<Guid, int>>(HttpMethod.Post, $"{Identity}/sessions/counts", new IdListRequest(userIds), cancellationToken);
}
