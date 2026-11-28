using Coworkee.Contracts.DataFilters;

namespace Coworkee.Application.Groups;

/// <summary>
/// The groups whose data filters the caller administers: <paramref name="TenantWide"/> for every group and role of the tenant, else the Group filters of
/// <paramref name="Readable"/> are listed and those of <paramref name="Editable"/> are written; Role filters need the whole tenant.
/// </summary>
public sealed record DataFilterReach(bool TenantWide, IReadOnlySet<Guid> Readable, IReadOnlySet<Guid> Editable)
{
    public static readonly DataFilterReach All = new(true, new HashSet<Guid>(), new HashSet<Guid>());
}

/// <summary>
/// Which Group and Role data filters the caller may read and write. Without the identity module every caller is tenant-wide; with group-scoped
/// administration a scoped administrator reads the filters of the groups he reaches and writes those below his own top groups.
/// </summary>
public interface IDataFilterReach
{
    Task<DataFilterReach> GetAsync(CancellationToken ct);

    /// <summary>Whether a group (Group layer) or role (Role layer) with this id exists in the current tenant.</summary>
    Task<bool> ExistsAsync(DataFilterLayer layer, Guid key, CancellationToken ct);
}
