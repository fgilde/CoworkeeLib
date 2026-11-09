using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

internal static class RoleNames
{
    public static readonly Error Taken = Error.Conflict("identity.role_name_taken", "A role with this name already exists.");

    public static readonly Error NotFound = Error.NotFound("identity.role_not_found", "Role not found.");

    public static readonly Error SystemRole = Error.Conflict("identity.role_system", "System roles cannot be changed.");

    public static readonly Error Fixed = Error.Conflict("identity.role_name_fixed", "Code refers to this role by its name; it cannot be renamed.");

    public static Task<bool> TakenAsync(CoworkeeDbContext db, string normalized, Guid? tenantId, Guid? except, CancellationToken cancellationToken) =>
        db.Set<Role>().AnyAsync(r => r.NormalizedName == normalized && (r.TenantId == null || r.TenantId == tenantId) && r.Id != except, cancellationToken);
}
