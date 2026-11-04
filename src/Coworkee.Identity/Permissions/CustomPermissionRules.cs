using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

/// <summary>
/// Custom permissions are defined for the whole installation like the ones in code, so only the system organisation's permission managers handle them;
/// the roles of every organisation grant them.
/// </summary>
internal static partial class CustomPermissionRules
{
    public static readonly Error HostOnly = Error.Forbidden("identity.permission_host_only", "Custom permissions are managed from the system organisation.");

    public static readonly Error NotFound = Error.NotFound("identity.permission_not_found", "The custom permission does not exist.");

    public static readonly Error CodeDefined = Error.Conflict("identity.permission_code_defined", "Permissions defined in code cannot be changed or deleted.");

    public static readonly Error Taken = Error.Conflict("identity.permission_taken", "A permission with this name exists already.");

    [GeneratedRegex(@"^Custom\.[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]+)*$")]
    public static partial Regex NamePattern();

    public static string GroupOf(string? group) => string.IsNullOrWhiteSpace(group) ? CustomPermissionNames.DefaultGroup : group.Trim();

    public static async Task<bool> IsHostAsync(ICurrentUser currentUser, ITenantDirectory tenants, CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);

    /// <summary>The stored permission of <paramref name="name"/>, or why the caller may not change it.</summary>
    public static async Task<Result<CustomPermission>> FindAsync(
        CoworkeeDbContext db, ICurrentUser currentUser, ITenantDirectory tenants, IPermissionDefinitionManager definitions, CustomPermissionDefinitions stored,
        string name, CancellationToken cancellationToken)
    {
        if (!await IsHostAsync(currentUser, tenants, cancellationToken))
        {
            return HostOnly;
        }

        // the manager keeps the stored instances, so a definition that is not one of them comes from code, which wins over a stored name
        if (definitions.All.FirstOrDefault(d => d.Name == name) is { } defined && !stored.Current.Any(s => ReferenceEquals(s, defined)))
        {
            return CodeDefined;
        }

        var permission = await db.Set<CustomPermission>().SingleOrDefaultAsync(p => p.Name == name, cancellationToken);
        return permission is null ? NotFound : Result<CustomPermission>.Success(permission);
    }
}
