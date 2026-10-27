using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Users;

/// <summary>A ticket for the auth server's <c>/Account/Impersonate</c>, which signs the caller in as <paramref name="UserId"/>.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(IdentityPermissions.Users.Impersonate)]
public sealed record StartImpersonation(Guid UserId) : ICommand<Result<ImpersonationTicketDto>>;

internal sealed class StartImpersonationValidator : AbstractValidator<StartImpersonation>
{
    public StartImpersonationValidator() => RuleFor(c => c.UserId).NotEmpty();
}

internal sealed class StartImpersonationHandler(ICurrentUser currentUser, ImpersonationRules rules, ImpersonationTickets tickets, ILogger<StartImpersonationHandler> logger)
    : IHandler<StartImpersonation, Result<ImpersonationTicketDto>>
{
    public async Task<Result<ImpersonationTicketDto>> HandleAsync(StartImpersonation command, CancellationToken cancellationToken)
    {
        // a service client holding the permission has no sign-in to switch
        if (currentUser.UserId is not { } me)
        {
            return ImpersonationRules.Forbidden;
        }

        if (await rules.CheckAsync(me, currentUser.ImpersonatorId, command.UserId, currentUser.TenantId, cancellationToken) is { } error)
        {
            return error;
        }

        logger.TicketIssued(me, command.UserId, currentUser.TenantId);
        return new ImpersonationTicketDto(tickets.Issue(new ImpersonationTicket(me, command.UserId, currentUser.TenantId)));
    }
}

/// <summary>Who may sign in as whom; checked when the ticket is issued, when the auth server redeems it and on every authorize and refresh of the impersonation.</summary>
public sealed class ImpersonationRules(CoworkeeDbContext db, IPermissionChecker checker, TimeProvider clock)
{
    internal static readonly Error Forbidden = Error.Forbidden("identity.impersonate_forbidden", "You may not sign in as other users.");

    private static readonly Error Self = Error.Conflict("identity.impersonate_self", "You are signed in as yourself already.");

    private static readonly Error Already = Error.Conflict("identity.already_impersonating", "Return to your own account before you sign in as another user.");

    private static readonly Error MorePermissions = Error.Forbidden("identity.impersonate_more_permissions", "The user holds permissions you do not have.");

    private static readonly Error OtherGroups = Error.Forbidden("identity.impersonate_other_groups", "The user belongs to groups you cannot access.");

    /// <summary>
    /// Null when <paramref name="impersonatorId"/> may sign in as <paramref name="targetId"/>: he holds the permission and is not signed in as someone else
    /// (<paramref name="currentImpersonator"/>), the target is another active, unlocked user of the tenant, holds no permission he lacks (all roles,
    /// whatever role either has picked; resource grants only where he holds the permission globally or on the same resource) and belongs only to groups
    /// he can access.
    /// </summary>
    public async Task<Error?> CheckAsync(Guid impersonatorId, Guid? currentImpersonator, Guid targetId, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (currentImpersonator is not null)
        {
            return Already;
        }

        if (targetId == impersonatorId)
        {
            return Self;
        }

        bool exists;
        var now = clock.GetUtcNow();
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId)))
        {
            exists = await db.Set<User>().AnyAsync(u => u.Id == targetId && u.TenantId == tenantId && u.IsActive && !(u.LockoutEnabled && u.LockoutEnd > now), cancellationToken);
        }

        if (!exists)
        {
            return UserErrors.NotFound;
        }

        // the full sets: the target can switch his active role, so a narrowed comparison would let him hold more than the impersonator
        var permissions = (PermissionChecker)checker;
        var mine = await permissions.GetGrantedForAsync(impersonatorId, tenantId, cancellationToken, narrow: false);
        if (!mine.Contains(IdentityPermissions.Users.Impersonate))
        {
            return Forbidden;
        }

        var theirs = await permissions.GetGrantedForAsync(targetId, tenantId, cancellationToken, narrow: false);
        if (theirs.Except(mine, StringComparer.Ordinal).Any())
        {
            return MorePermissions;
        }

        var myResources = await permissions.GetResourceGrantsForAsync(impersonatorId, tenantId, cancellationToken);
        if ((await permissions.GetResourceGrantsForAsync(targetId, tenantId, cancellationToken)).Any(g => !mine.Contains(g.Permission) && !myResources.Contains(g)))
        {
            return MorePermissions;
        }

        return mine.Contains(IdentityPermissions.Groups.ScopeAll) || await InAccessibleGroupsAsync(impersonatorId, targetId, tenantId, cancellationToken) ? null : OtherGroups;
    }

    // accessible as IGroupScope computes it: the impersonator's groups and their subtrees, by code
    private async Task<bool> InAccessibleGroupsAsync(Guid impersonatorId, Guid targetId, Guid? tenantId, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        // a join, not g.Members.Where(...): SQLite has no APPLY
        var groups = await (from member in db.Set<UserGroupMember>()
                            where member.UserId == impersonatorId || member.UserId == targetId
                            join g in db.Set<UserGroup>() on member.GroupId equals g.Id
                            select new { member.UserId, g.Id, g.Code }).AsNoTracking().ToListAsync(cancellationToken);
        var mine = groups.Where(g => g.UserId == impersonatorId).ToList();
        return groups.Where(g => g.UserId == targetId).All(target => mine.Any(own =>
            own.Id == target.Id || (own.Code is { } code && target.Code is { } targetCode && GroupCodes.IsDescendant(targetCode, code))));
    }
}

public sealed record ImpersonationTicket(Guid ImpersonatorId, Guid TargetId, Guid? TenantId);

/// <summary>Protects tickets with the key ring all hosts share, so the auth server reads what the API issued; a ticket lives a minute.</summary>
public sealed class ImpersonationTickets(IDataProtectionProvider provider, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("Coworkee.Impersonation.v1").ToTimeLimitedDataProtector();

    public string Issue(ImpersonationTicket ticket) => _protector.Protect(JsonSerializer.Serialize(ticket), clock.GetUtcNow() + Lifetime);

    /// <summary>The ticket, or null when it is expired, altered or no ticket at all.</summary>
    public ImpersonationTicket? Read(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ImpersonationTicket>(_protector.Unprotect(value));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}

public static partial class ImpersonationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {ImpersonatorId} got a ticket to sign in as {UserId} in tenant {TenantId}")]
    public static partial void TicketIssued(this ILogger logger, Guid impersonatorId, Guid userId, Guid? tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {ImpersonatorId} signed in as {UserId} in tenant {TenantId}")]
    public static partial void Started(this ILogger logger, Guid impersonatorId, Guid userId, Guid? tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {ImpersonatorId} returned from {UserId}; {Revoked} authorization(s) revoked")]
    public static partial void Stopped(this ILogger logger, Guid impersonatorId, Guid userId, int revoked);
}
