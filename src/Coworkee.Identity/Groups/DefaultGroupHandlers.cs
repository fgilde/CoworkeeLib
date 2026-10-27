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

internal sealed class DefaultGroupHandlers(CoworkeeDbContext db) : IHandler<GetDefaultGroup, Result<DefaultGroupDto>>, IHandler<SetDefaultGroup, Result>
{
    public async Task<Result<DefaultGroupDto>> HandleAsync(GetDefaultGroup query, CancellationToken cancellationToken) =>
        new DefaultGroupDto(await db.Set<UserGroup>().Where(g => g.IsDefault).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(cancellationToken));

    // a concurrent change of the default group: the unique index (or a deadlock between the two transactions) refuses one of them
    private static readonly Error Conflict = Error.Conflict("identity.default_group_conflict", "The default group was changed at the same time. Try again.");

    public async Task<Result> HandleAsync(SetDefaultGroup command, CancellationToken cancellationToken)
    {
        try
        {
            return await SetAsync(command.GroupId, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or System.Data.Common.DbException { IsTransient: true })
        {
            return Conflict;
        }
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
