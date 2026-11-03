using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class ImportGroupHandler(CoworkeeDbContext db, Users.AdminReach reach) : IHandler<ImportGroup, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(ImportGroup command, CancellationToken cancellationToken)
    {
        var groups = db.Set<UserGroup>();
        Guid? parentId = null;
        foreach (var segment in (command.Row.ParentPath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var current = parentId;
            parentId = await groups.Where(g => g.ParentId == current && g.Name == segment).Select(g => (Guid?)g.Id).SingleOrDefaultAsync(cancellationToken);
            if (parentId is null)
            {
                return GroupErrors.ParentPathUnknown;
            }
        }

        // a group-scoped administrator imports below the groups of his reach; a parent out of it is as unknown as a missing one (paths start at the roots)
        if (await reach.CheckParentAsync(parentId, cancellationToken) is { } refused)
        {
            return parentId is null ? refused : GroupErrors.ParentPathUnknown;
        }

        var name = command.Row.Name.Trim();
        var existing = await groups.SingleOrDefaultAsync(g => g.ParentId == parentId && g.Name == name, cancellationToken);
        if (existing is null)
        {
            return await CreateGroupHandler.AddAsync(db, new GroupRequest(name, command.Row.Description?.Trim(), parentId), cancellationToken);
        }

        // the default group below a group in reach
        if (!await reach.ReachesGroupAsync(existing.Id, cancellationToken))
        {
            return Users.AdminReach.AllGroupsRequired;
        }

        if (!string.IsNullOrWhiteSpace(command.Row.Description))
        {
            existing.Description = command.Row.Description.Trim();
        }

        return existing.Id;
    }
}
