using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Reporting;

/// <summary>
/// A report is seen by its owner, by everybody of the organisation, by the holders of one of its roles or by the members of one of its groups and of the
/// groups above them (the user's active groups, so a group selection narrows the list); only the owner changes it.
/// </summary>
internal sealed class ReportAccess(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IGroupScope groups)
{
    public Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Reports need a signed-in user.");

    public async Task<IQueryable<Report>> VisibleAsync(CancellationToken cancellationToken)
    {
        var me = Me;
        var roles = (await permissions.GetRoleIdsAsync(cancellationToken)).ToList();
        var active = (await groups.ActiveAsync(cancellationToken)).ToList();
        return db.Set<Report>().Where(r => r.OwnerId == me
            || r.Visibility == ReportVisibility.Organisation
            || (r.Visibility == ReportVisibility.Roles && r.RoleIds.Any(id => roles.Contains(id)))
            || (r.Visibility == ReportVisibility.Groups && r.GroupIds.Any(id => active.Contains(id))));
    }

    public Task<bool> MayShareAsync(CancellationToken cancellationToken) => permissions.IsGrantedAsync(ReportPermissions.Share, cancellationToken);

    /// <summary>The groups the user may share a report with: the user's groups and those below them.</summary>
    public Task<IReadOnlyList<Guid>> ShareableGroupsAsync(CancellationToken cancellationToken) => groups.AccessibleAsync(cancellationToken);

    public Task<IReadOnlyList<GroupNode>> GroupNodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) => groups.NodesAsync(ids, cancellationToken);

    public bool Owns(Report report) => report.OwnerId == Me;
}
