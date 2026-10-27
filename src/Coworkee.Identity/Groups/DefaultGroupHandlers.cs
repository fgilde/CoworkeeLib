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

    public async Task<Result> HandleAsync(SetDefaultGroup command, CancellationToken cancellationToken)
    {
        // the tenant filter keeps this to the caller's groups
        var groups = await db.Set<UserGroup>().Where(g => g.IsDefault || g.Id == command.GroupId).ToListAsync(cancellationToken);
        if (command.GroupId is { } id && groups.All(g => g.Id != id))
        {
            return GroupErrors.NotFound;
        }

        var old = groups.Where(g => g.IsDefault && g.Id != command.GroupId).ToList();
        var next = groups.FirstOrDefault(g => g.Id == command.GroupId && !g.IsDefault);
        old.ForEach(g => g.IsDefault = false);
        if (next is null)
        {
            return Result.Success();
        }

        // EF does not order updates by a filtered unique index: the old default is saved first, in one transaction with the new one
        await using var transaction = old.Count > 0 && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (old.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        next.IsDefault = true;
        if (transaction is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }
}
