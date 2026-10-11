using Coworkee.Contracts.Reporting;
using Coworkee.Domain;

namespace Coworkee.Reporting;

[Realtime(ReportPermissions.View)]
public sealed class Report : AuditedEntity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public ReportVisibility Visibility { get; set; }

    public List<Guid> RoleIds { get; set; } = [];

    /// <summary>The <see cref="ReportDefinition"/> as JSON.</summary>
    public required string Definition { get; set; }
}

public sealed class ReportFavorite
{
    public Guid ReportId { get; set; }

    public Guid UserId { get; set; }
}
