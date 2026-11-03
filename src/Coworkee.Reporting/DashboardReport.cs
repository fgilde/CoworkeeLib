using Coworkee.Contracts.Reporting;
using Coworkee.Domain;

namespace Coworkee.Reporting;

/// <summary>The report a user sees as dashboard; without <see cref="UserId"/> the default of the organisation.</summary>
[Realtime(ReportPermissions.View)]
[NotAudited]
public sealed class DashboardReport : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }

    public Guid ReportId { get; set; }
}
