using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
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

/// <summary>Who may sign in as whom; checked when the ticket is issued and again when the auth server redeems it.</summary>
public sealed class ImpersonationRules(CoworkeeDbContext db, IPermissionChecker checker)
{
    internal static readonly Error Forbidden = Error.Forbidden("identity.impersonate_forbidden", "You may not sign in as other users.");

    private static readonly Error Self = Error.Conflict("identity.impersonate_self", "You are signed in as yourself already.");

    private static readonly Error Already = Error.Conflict("identity.already_impersonating", "Return to your own account before you sign in as another user.");

    private static readonly Error MorePermissions = Error.Forbidden("identity.impersonate_more_permissions", "The user holds permissions you do not have.");

    /// <summary>
    /// Null when <paramref name="impersonatorId"/> may sign in as <paramref name="targetId"/>: he holds the permission and is not signed in as someone else
    /// (<paramref name="currentImpersonator"/>), the target is another active user of the tenant and holds no permission he lacks.
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
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId)))
        {
            exists = await db.Set<User>().AnyAsync(u => u.Id == targetId && u.TenantId == tenantId && u.IsActive, cancellationToken);
        }

        if (!exists)
        {
            return UserErrors.NotFound;
        }

        // both sets the same way, so an active role narrows both
        var permissions = (PermissionChecker)checker;
        var mine = await permissions.GetGrantedForAsync(impersonatorId, tenantId, cancellationToken);
        if (!mine.Contains(IdentityPermissions.Users.Impersonate))
        {
            return Forbidden;
        }

        var theirs = await permissions.GetGrantedForAsync(targetId, tenantId, cancellationToken);
        return theirs.Except(mine, StringComparer.Ordinal).Any() ? MorePermissions : null;
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
