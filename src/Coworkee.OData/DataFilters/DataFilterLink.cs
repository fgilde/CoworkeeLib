using Coworkee.Contracts.DataFilters;
using Coworkee.Domain;

namespace Coworkee.OData.DataFilters;

/// <summary>System-wide: a row of <see cref="EntitySet"/> is visible only when the <see cref="TargetSet"/> row with the key <see cref="Property"/> passes the target's filters.</summary>
[Realtime(DataFilterPermissions.ManageSystem)]
public sealed class DataFilterLink : AuditedEntity, IHasRealtimeTopics
{
    public string EntitySet { get; set; } = string.Empty;

    public string Property { get; set; } = string.Empty;

    public string TargetSet { get; set; } = string.Empty;

    public IEnumerable<string> RealtimeTopics => [Coworkee.Contracts.Data.ClientEntityTopics.Changed];
}
