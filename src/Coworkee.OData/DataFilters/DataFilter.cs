using Coworkee.Contracts.DataFilters;
using Coworkee.Domain;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// A hidden row filter of one OData set for one layer. <see cref="TenantId"/> is null for the System layer; Tenant, Group and Role filters belong to their organisation.
/// </summary>
public sealed class DataFilter : AuditedEntity, IHasRealtimeSignals
{
    public Guid? TenantId { get; set; }

    public string EntitySet { get; set; } = string.Empty;

    public DataFilterLayer Layer { get; set; }

    /// <summary>The group or role id; null for System and Tenant.</summary>
    public Guid? LayerKey { get; set; }

    /// <summary>A <c>QueryGroup</c> as JSON.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>False: key reads (details) skip this filter.</summary>
    public bool AppliesToDetails { get; set; } = true;

    /// <summary>Counts the saves, so caches and copies can tell a changed filter.</summary>
    public int Version { get; set; }

    // a payload-free signal for everyone it applies to: the organisation's members, or every organisation for a System filter (tables and admin views
    // reload, client copies synchronise again); nothing tells a user what changed
    public IEnumerable<string> RealtimeSignals => [TenantId is null ? DataFilterTopics.Changed : DataFilterTopics.TenantChanged];
}
