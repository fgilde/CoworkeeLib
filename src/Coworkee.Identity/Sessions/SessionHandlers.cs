using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Sessions;

/// <summary>
/// The sessions of the organisation (the tenant filter) and of the users in the caller's reach. Ending one only marks its row: the saved change revokes its
/// tokens and signs its browser out at once (<see cref="SessionChangeInterceptor"/>).
/// </summary>
internal sealed class SessionHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, AdminReach reach, AccountGuard guard, IHttpContextAccessor http, TimeProvider clock)
    : IHandler<GetMySessions, Result<IReadOnlyList<UserSessionDto>>>, IHandler<EndSession, Result>, IHandler<GetUserSessions, Result<UserSessionsDto>>,
      IHandler<GetSessionCounts, Result<IReadOnlyDictionary<Guid, int>>>, IHandler<SetSessionLimit, Result>
{
    private static readonly Error NotFound = Error.NotFound("identity.session_not_found", "The session does not exist or has ended.");

    private static readonly Error Own = Error.Conflict("identity.own_session", "Use the sign-out of the user menu for your own session.");

    private static readonly Error FromKey = Error.Forbidden("identity.sessions_from_key", "API keys cannot end sessions; sign in to the app.");

    private string? CurrentSid => http.HttpContext?.User.FindFirst(SessionStamp.SessionClaimType)?.Value;

    private IQueryable<UserSession> Active => db.Set<UserSession>().Where(UserSession.IsActive(clock.GetUtcNow()));

    public async Task<Result<IReadOnlyList<UserSessionDto>>> HandleAsync(GetMySessions query, CancellationToken cancellationToken)
    {
        var sessions = await Active.AsNoTracking().Where(s => s.UserId == currentUser.UserId).OrderByDescending(s => s.LastSeenAt).ToListAsync(cancellationToken);
        return sessions.Select(s => s.ToDto(CurrentSid)).ToList();
    }

    public async Task<Result> HandleAsync(EndSession command, CancellationToken cancellationToken)
    {
        if (currentUser.ApiKeyId is not null)
        {
            return FromKey;
        }

        var session = await db.Set<UserSession>().SingleOrDefaultAsync(s => s.Id == command.Id && s.EndedAt == null, cancellationToken);
        var mine = session?.UserId == currentUser.UserId;
        if (session is null || (!mine && !await permissions.IsGrantedAsync(IdentityPermissions.Sessions.Manage, cancellationToken)))
        {
            return NotFound;
        }

        if (session.Sid == CurrentSid)
        {
            return Own;
        }

        if (await guard.CheckUserAsync(session.UserId, cancellationToken) is { } refused)
        {
            return refused;
        }

        session.End(clock.GetUtcNow(), mine ? SessionEndReasons.EndedByUser : SessionEndReasons.Ended, currentUser.UserId);
        return Result.Success();
    }

    public async Task<Result<UserSessionsDto>> HandleAsync(GetUserSessions query, CancellationToken cancellationToken)
    {
        var users = await reach.FilterUsersAsync(db.Set<User>().Where(u => u.Id == query.UserId && u.TenantId == currentUser.TenantId), cancellationToken);
        if (await users.Select(u => new { u.MaxSessions }).SingleOrDefaultAsync(cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        var sessions = await Active.AsNoTracking().Where(s => s.UserId == query.UserId).OrderByDescending(s => s.LastSeenAt).ToListAsync(cancellationToken);
        return new UserSessionsDto(user.MaxSessions, [.. sessions.Select(s => s.ToDto(CurrentSid))]);
    }

    public async Task<Result<IReadOnlyDictionary<Guid, int>>> HandleAsync(GetSessionCounts query, CancellationToken cancellationToken)
    {
        var ids = query.UserIds.Distinct().ToList();
        var users = (await reach.FilterUsersAsync(db.Set<User>(), cancellationToken)).Where(u => ids.Contains(u.Id)).Select(u => u.Id);
        return await Active.Where(s => users.Contains(s.UserId)).GroupBy(s => s.UserId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
    }

    public async Task<Result> HandleAsync(SetSessionLimit command, CancellationToken cancellationToken)
    {
        if (await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.UserId && u.TenantId == currentUser.TenantId, cancellationToken) is not { } user)
        {
            return UserErrors.NotFound;
        }

        if (await guard.CheckUserAsync(user.Id, cancellationToken) is { } refused)
        {
            return refused;
        }

        user.MaxSessions = command.MaxSessions;
        return Result.Success();
    }
}
