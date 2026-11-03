using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class SetUserRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, AccountGuard guard) : IHandler<SetUserRoles, Result>
{
    public async Task<Result> HandleAsync(SetUserRoles command, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        if (await guard.CheckUserAsync(command.Id, cancellationToken) is { } refused)
        {
            return refused;
        }

        var wanted = command.RoleIds.Distinct().ToList();
        var roles = await db.Set<Role>().Where(r => wanted.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId)).ToListAsync(cancellationToken);
        if (roles.Count != wanted.Count)
        {
            return Error.Validation(nameof(command.RoleIds), "Unknown role.");
        }

        var adminRoleId = await AdminGuard.AdminRoleIdAsync(db, cancellationToken);
        var current = await db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == command.Id).ToListAsync(cancellationToken);
        var hadAdmin = current.Any(r => r.RoleId == adminRoleId);
        var changed = wanted.Where(id => current.All(r => r.RoleId != id)).Concat(current.Where(r => !wanted.Contains(r.RoleId)).Select(r => r.RoleId));
        if (await guard.CheckRolesAsync(changed, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        if (hadAdmin && !wanted.Contains(adminRoleId) && !await AdminGuard.OtherActiveAdminExistsAsync(db, currentUser.TenantId, command.Id, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        db.RemoveRange(current.Where(r => !wanted.Contains(r.RoleId)));
        db.AddRange(wanted.Where(id => current.All(r => r.RoleId != id)).Select(id => new IdentityUserRole<Guid> { UserId = command.Id, RoleId = id }));
        return Result.Success();
    }
}
