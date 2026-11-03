using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal static class AdminGuard
{
    public static readonly Error LastAdmin = Error.Conflict("identity.last_admin", "The last active administrator cannot be removed or deactivated.");

    public static readonly Error AdminRoleRestricted = Error.Forbidden("identity.admin_role_restricted", "Only administrators can grant or revoke the administrator role.");

    public static Task<Guid> AdminRoleIdAsync(CoworkeeDbContext db, CancellationToken cancellationToken) =>
        db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync(cancellationToken);

    public static async Task<bool> IsAdminAsync(CoworkeeDbContext db, Guid? userId, CancellationToken cancellationToken)
    {
        var adminRoleId = await AdminRoleIdAsync(db, cancellationToken);
        return await db.Set<IdentityUserRole<Guid>>().AnyAsync(r => r.UserId == userId && r.RoleId == adminRoleId, cancellationToken);
    }

    public static async Task<bool> OtherActiveAdminExistsAsync(CoworkeeDbContext db, Guid? tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var adminRoleId = await AdminRoleIdAsync(db, cancellationToken);
        return await (from userRole in db.Set<IdentityUserRole<Guid>>()
                      join user in db.Set<User>() on userRole.UserId equals user.Id
                      where userRole.RoleId == adminRoleId && user.Id != userId && user.TenantId == tenantId && user.IsActive
                      select user.Id).AnyAsync(cancellationToken);
    }
}
