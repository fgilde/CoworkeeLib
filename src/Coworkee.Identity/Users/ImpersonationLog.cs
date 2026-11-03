using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Users;

public static partial class ImpersonationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {ImpersonatorId} got a ticket to sign in as {UserId} in tenant {TenantId}")]
    public static partial void TicketIssued(this ILogger logger, Guid impersonatorId, Guid userId, Guid? tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {ImpersonatorId} signed in as {UserId} in tenant {TenantId}")]
    public static partial void Started(this ILogger logger, Guid impersonatorId, Guid userId, Guid? tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {ImpersonatorId} returned from {UserId}; {Revoked} authorization(s) revoked")]
    public static partial void Stopped(this ILogger logger, Guid impersonatorId, Guid userId, int revoked);
}
