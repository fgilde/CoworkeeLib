using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Security;

/// <summary>Sessions (sign-ins) of the signed-in user and, for administrators, of others; the admin list reads the UserSessions OData set.</summary>
public interface ISessionsApi
{
    Task<IReadOnlyList<UserSessionDto>> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>Ends one of the user's other sessions, or with <c>Identity.Sessions.Manage</c> one of someone else.</summary>
    Task EndAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UserSessionsDto> GetOfUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task EndAllAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>null takes the organisation's setting, 0 is unlimited.</summary>
    Task SetLimitAsync(Guid userId, int? maxSessions, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> GetCountsAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);
}
