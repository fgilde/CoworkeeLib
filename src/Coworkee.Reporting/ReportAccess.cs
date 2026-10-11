using Coworkee.Application.Authorization;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Reporting;

/// <summary>A report is seen by its owner, by everybody of the organisation or by the holders of one of its roles; only the owner changes it.</summary>
internal sealed class ReportAccess(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions)
{
    public Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Reports need a signed-in user.");

    public async Task<IQueryable<Report>> VisibleAsync(CancellationToken cancellationToken)
    {
        var me = Me;
        var roles = (await permissions.GetRoleIdsAsync(cancellationToken)).ToList();
        return db.Set<Report>().Where(r => r.OwnerId == me
            || r.Visibility == ReportVisibility.Organisation
            || (r.Visibility == ReportVisibility.Roles && r.RoleIds.Any(id => roles.Contains(id))));
    }

    public Task<bool> MayShareAsync(CancellationToken cancellationToken) => permissions.IsGrantedAsync(ReportPermissions.Share, cancellationToken);

    public bool Owns(Report report) => report.OwnerId == Me;
}
