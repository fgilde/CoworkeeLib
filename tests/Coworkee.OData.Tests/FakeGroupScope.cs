using Coworkee.Application.Groups;
using Microsoft.AspNetCore.Http;

namespace Coworkee.OData.Tests;

/// <summary>
/// Every user has access to the units <see cref="A"/> and <see cref="B"/> (or <see cref="Accessible"/>), whose tree is <see cref="Nodes"/>; <see cref="Stored"/> is the selection saved by the user,
/// which applies inside requests only (as for interactive sign-ins), <see cref="GroupScope"/> switches as in the real scope.
/// </summary>
public sealed class FakeGroupScope(IHttpContextAccessor http) : IGroupScope
{
    public static readonly Guid A = Guid.Parse("0197a000-0000-7000-8000-00000000000a");

    public static readonly Guid B = Guid.Parse("0197a000-0000-7000-8000-00000000000b");

    public IReadOnlyList<Guid> Accessible { get; set; } = [A, B];

    public IReadOnlyList<GroupNode> Nodes { get; set; } = [];

    public IReadOnlyList<Guid> Stored { get; set; } = [];

    public Task<IReadOnlyList<Guid>> MembershipsAsync(CancellationToken ct) => Task.FromResult(Accessible);

    public Task<IReadOnlyList<Guid>> AccessibleAsync(CancellationToken ct) => Task.FromResult(Accessible);

    public Task<IReadOnlyList<Guid>> SelectedAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(Accessible.Intersect(GroupScope.SelectionOverride ?? (http.HttpContext is null ? [] : Stored)).ToList());

    public async Task<IReadOnlyList<Guid>> ActiveAsync(CancellationToken ct) =>
        GroupScope.IsUnnarrowed || await SelectedAsync(ct) is not { Count: > 0 } selected ? Accessible : selected;

    public Task<IReadOnlyList<GroupNode>> NodesAsync(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<GroupNode>>(Nodes.Where(n => ids.Contains(n.Id)).ToList());

    public Task<IReadOnlyList<GroupNode>> AncestorsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<GroupNode>>([]);

    public Task<Guid?> DefaultGroupIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);

    public Task<IReadOnlyList<Guid>> OwnersAsync(Guid groupId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
}
