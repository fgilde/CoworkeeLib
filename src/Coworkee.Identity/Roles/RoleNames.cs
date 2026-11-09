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

    /// <summary>The role is in <see cref="Users.IdentityAdministrationOptions.FixedRoleNames"/>: <paramref name="usedBy"/> finds it by its name.</summary>
    public static Error Fixed(string usedBy, bool deleting) => Error.Conflict("identity.role_name_fixed",
        deleting ? "Code finds this role by its name ({0}); it cannot be deleted." : "Code finds this role by its name ({0}); it cannot be renamed.", usedBy);

    public static Task<bool> TakenAsync(CoworkeeDbContext db, string normalized, Guid? tenantId, Guid? except, CancellationToken cancellationToken) =>
        db.Set<Role>().AnyAsync(r => r.NormalizedName == normalized && (r.TenantId == null || r.TenantId == tenantId) && r.Id != except, cancellationToken);
}
