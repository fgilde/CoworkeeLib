using Coworkee.Application.Groups;
using Coworkee.OData.ClientEntities;

namespace Coworkee.Identity.Groups;

/// <summary>The selected groups: the mirror holds the rows the lists show, so it starts over when the selection changes.</summary>
internal sealed class GroupScopePartitionPart(IGroupScope groups) : IClientEntityPartitionPart
{
    public async Task<string?> GetAsync(CancellationToken ct) =>
        await groups.SelectedAsync(ct) is { Count: > 0 } selected ? string.Join(",", selected.Order()) : null;
}
