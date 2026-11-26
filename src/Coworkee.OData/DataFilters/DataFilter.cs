using Coworkee.Contracts.DataFilters;
using Coworkee.Domain;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// A hidden row filter of one OData set for one layer. <see cref="TenantId"/> is null for the System layer; Tenant, Group and Role filters belong to their organisation.
/// </summary>
public sealed class DataFilter : AuditedEntity, IHasRealtimeTopics
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

    // global topics reach every organisation (System filters belong to none) and every kind of manager: tables and admin views reload, client copies sync again
    public IEnumerable<string> RealtimeTopics => [DataFilterTopics.Changed, Coworkee.Contracts.Data.ClientEntityTopics.Changed];
}
