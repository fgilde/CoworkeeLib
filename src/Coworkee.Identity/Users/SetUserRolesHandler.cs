using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class SetUserRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, AccountGuard guard)
    : IHandler<SetUserRoles, Result>, IHandler<AddUserRole, Result>, IHandler<RemoveUserRole, Result>
{
    public Task<Result> HandleAsync(SetUserRoles command, CancellationToken cancellationToken)
    {
        var wanted = command.RoleIds.Distinct().ToList();
        return ChangeAsync(command.Id, wanted, current => [.. current.Except(wanted)], nameof(command.RoleIds), cancellationToken);
    }

    public Task<Result> HandleAsync(AddUserRole command, CancellationToken cancellationToken) =>
        ChangeAsync(command.Id, [command.RoleId], _ => [], nameof(command.RoleId), cancellationToken);

    public Task<Result> HandleAsync(RemoveUserRole command, CancellationToken cancellationToken) =>
        ChangeAsync(command.Id, [], _ => [command.RoleId], nameof(command.RoleId), cancellationToken);

    // only the named roles change: a single add or remove keeps what another administrator changed meanwhile
    private async Task<Result> ChangeAsync(Guid userId, List<Guid> add, Func<List<Guid>, List<Guid>> remove, string field, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == userId && u.TenantId == currentUser.TenantId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        if (await guard.CheckUserAsync(userId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var current = await db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == userId).ToListAsync(cancellationToken);
        var currentIds = current.Select(r => r.RoleId).ToList();
        var added = add.Except(currentIds).ToList();
        var removed = remove(currentIds).Intersect(currentIds).ToList();
        if (await db.Set<Role>().CountAsync(r => added.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken) != added.Count)
        {
            return Error.Validation(field, "Unknown role.");
        }

        if (await guard.CheckRolesAsync(added.Concat(removed), cancellationToken) is { } restricted)
        {
            return restricted;
        }

        if (removed.Contains(await AdminGuard.AdminRoleIdAsync(db, cancellationToken))
            && !await AdminGuard.OtherActiveAdminExistsAsync(db, currentUser.TenantId, userId, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        db.RemoveRange(current.Where(r => removed.Contains(r.RoleId)));
        db.AddRange(added.Select(id => new IdentityUserRole<Guid> { UserId = userId, RoleId = id }));
        return Result.Success();
    }
}
