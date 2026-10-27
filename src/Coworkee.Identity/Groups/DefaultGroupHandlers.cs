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

        foreach (var group in groups)
        {
            group.IsDefault = group.Id == command.GroupId;
        }

        return Result.Success();
    }
}
