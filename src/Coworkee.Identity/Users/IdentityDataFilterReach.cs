using Coworkee.Application.Groups;
using Coworkee.Contracts.DataFilters;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

/// <summary>
/// Data filter reach from <see cref="AdminReach"/>: tenant-wide for the option off, the system, administrators and <c>Identity.Groups.ScopeAll</c>
/// (a member of every group stays scoped); else the scoped groups are read and all but the caller's own top groups written.
/// </summary>
internal sealed class IdentityDataFilterReach(AdminReach reach, IGroupScope scope, CoworkeeDbContext db, ICurrentUser currentUser) : IDataFilterReach
{
    public async Task<DataFilterReach> GetAsync(CancellationToken ct)
    {
        if (await reach.ScopedGroupIdsAsync(ct) is not { } scoped)
        {
            return DataFilterReach.All;
        }

        var editable = scoped.ToHashSet();
        editable.ExceptWith((await reach.RootsAsync(ct))!.Select(r => r.Id));
        return new DataFilterReach(false, scoped.ToHashSet(), editable);
    }

    public async Task<bool> ExistsAsync(DataFilterLayer layer, Guid key, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        return layer switch
        {
            DataFilterLayer.Group => (await scope.NodesAsync([key], ct)).Count > 0,
            DataFilterLayer.Role => await db.Set<Role>().AnyAsync(r => r.Id == key && (r.TenantId == null || r.TenantId == tenantId), ct),
            _ => true,
        };
    }
}
