using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

/// <summary>The default group of the tenant; every signed-in user may ask, apps need it in their filters.</summary>
public sealed record GetDefaultGroup : IQuery<Result<DefaultGroupDto>>;

/// <summary>Makes the group the one new users join; null leaves the tenant without a default group.</summary>
[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record SetDefaultGroup(Guid? GroupId) : ICommand<Result>;

internal sealed class DefaultGroupHandlers(CoworkeeDbContext db, Roles.RoleOffer offer) : IHandler<GetDefaultGroup, Result<DefaultGroupDto>>, IHandler<SetDefaultGroup, Result>
{
    public async Task<Result<DefaultGroupDto>> HandleAsync(GetDefaultGroup query, CancellationToken cancellationToken) =>
        new DefaultGroupDto(await db.Set<UserGroup>().Where(g => g.IsDefault).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(cancellationToken));

    // a concurrent change of the default group: the unique index (or a deadlock between the two transactions) refuses one of them
    private static readonly Error Conflict = Error.Conflict("identity.default_group_conflict", "The default group was changed at the same time. Try again.");

    public async Task<Result> HandleAsync(SetDefaultGroup command, CancellationToken cancellationToken)
    {
        // inside a transaction of the caller the clash is its business: Postgres has aborted that transaction, so the error goes up
        var own = db.Database.CurrentTransaction is null;
        try
        {
            return await SetAsync(command.GroupId, cancellationToken);
        }
        catch (Exception exception) when (own && IsClash(exception))
        {
            return Conflict;
        }
    }

    /// <summary>A unique index violation or a deadlock, also wrapped (Npgsql's execution strategy throws "likely due to a transient failure" around it).</summary>
    internal static bool IsClash(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is Npgsql.PostgresException { SqlState: "23505" or "40P01" }
                or Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 or 1205 }
                or Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 })
            {
                return true;
            }
        }

        return false;
    }

    // saved here, not by the pipeline, so a clash becomes a 409: EF does not order updates by a filtered unique index, so the old default is
    // saved first, in one transaction with the new one
    private async Task<Result> SetAsync(Guid? groupId, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

        // the tenant filter keeps this to the caller's groups
        var groups = await db.Set<UserGroup>().Where(g => g.IsDefault || g.Id == groupId).ToListAsync(cancellationToken);
        if (groupId is { } id && groups.All(g => g.Id != id))
        {
            return GroupErrors.NotFound;
        }

        // new users will get the roles and the grants of the group
        if (groups.FirstOrDefault(g => g.Id == groupId && !g.IsDefault) is { } candidate)
        {
            var grants = await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Group && g.ProviderKey == candidate.Id)
                .Select(g => g.Name).ToListAsync(cancellationToken);
            if ((candidate.Roles.Count > 0 || grants.Count > 0) && await offer.RefusedAsync([.. candidate.Roles.Select(r => r.RoleId)], grants, cancellationToken) is { } refused)
            {
                return refused;
            }
        }

        foreach (var group in groups.Where(g => g.IsDefault && g.Id != groupId))
        {
            group.IsDefault = false;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (groups.FirstOrDefault(g => g.Id == groupId && !g.IsDefault) is { } next)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }
}
