using System.Linq.Expressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

// ponytail: memberships, access and the stored selection are read once per scope (request), no cross-request cache;
// add one with the permission cache's invalidation pattern if it shows up in profiles
internal sealed class GroupScopeService(CoworkeeDbContext db, ICurrentUser user, IPermissionChecker permissions, IHttpContextAccessor http) : IGroupScope
{
    internal static readonly Expression<Func<UserGroup, GroupNode>> Node = g => new GroupNode(g.Id, g.ParentId, g.Code ?? "", g.Name);

    // keyed by the acting user: a CurrentUserScope inside the same DI scope must not reuse another user's groups
    private readonly Dictionary<(Guid?, Guid?), Access> _access = [];
    private readonly Dictionary<(Guid?, Guid?), IReadOnlyList<Guid>> _stored = [];

    private (Guid?, Guid?) Actor => (user.UserId, user.TenantId);

    private IQueryable<UserGroup> Groups => db.Set<UserGroup>().AsNoTracking();

    // interactive sign-ins only: personal API keys (MCP), service clients and jobs without a request see everything accessible
    private bool SelectionApplies => http.HttpContext is not null && user.ApiKeyId is null && user.ClientId is null;

    public async Task<IReadOnlyList<Guid>> MembershipsAsync(CancellationToken ct) => (await AccessAsync(ct)).Memberships;

    public async Task<IReadOnlyList<Guid>> AccessibleAsync(CancellationToken ct) => (await AccessAsync(ct)).Ids;

    public async Task<IReadOnlyList<Guid>> SelectedAsync(CancellationToken ct)
    {
        var access = await AccessAsync(ct);
        var wanted = GroupScope.SelectionOverride ?? (SelectionApplies && access.Ids.Count > 0 ? await StoredAsync(ct) : []);
        return access.Ids.Intersect(wanted).ToList();
    }

    public async Task<IReadOnlyList<Guid>> ActiveAsync(CancellationToken ct)
    {
        var access = await AccessAsync(ct);
        IReadOnlyList<Guid> selected = GroupScope.IsUnnarrowed ? [] : await SelectedAsync(ct);
        if (selected.Count == 0)
        {
            return access.Ids;
        }

        var codes = access.Groups.Where(g => selected.Contains(g.Id) && g.Code is not null).Select(g => g.Code!).ToList();
        return access.Groups
            .Where(g => selected.Contains(g.Id) || (g.Code is { } code && codes.Any(ancestor => GroupCodes.IsDescendant(code, ancestor))))
            .Select(g => g.Id).ToList();
    }

    public async Task<IReadOnlyList<GroupNode>> NodesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var nodes = await Groups.Where(g => EF.Parameter(wanted).Contains(g.Id)).Select(Node).ToListAsync(ct);
        return nodes.OrderBy(n => n.Code, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<GroupNode>> AncestorsAsync(Guid id, CancellationToken ct)
    {
        if (await Groups.Where(g => g.Id == id).Select(g => g.Code).SingleOrDefaultAsync(ct) is not { } code)
        {
            return [];
        }

        var prefixes = GroupCodes.Prefixes(code).SkipLast(1).ToList();
        var nodes = await Groups.Where(g => EF.Parameter(prefixes).Contains(g.Code!)).Select(Node).ToListAsync(ct);
        return nodes.OrderBy(n => n.Code, StringComparer.Ordinal).ToList();
    }

    public Task<Guid?> DefaultGroupIdAsync(CancellationToken ct) => Groups.Where(g => g.IsDefault).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> OwnersAsync(Guid groupId, CancellationToken ct) =>
        await Groups.Where(g => g.Id == groupId).SelectMany(g => g.Members).Where(m => m.IsOwner).Select(m => m.UserId).ToListAsync(ct);

    private async Task<Access> AccessAsync(CancellationToken ct)
    {
        if (_access.TryGetValue(Actor, out var cached))
        {
            return cached;
        }

        if (user.UserId is not { } me)
        {
            return _access[Actor] = new Access([], []);
        }

        var memberships = await Groups.Where(g => g.Members.Any(m => m.UserId == me)).Select(g => new GroupRef(g.Id, g.Code)).ToListAsync(ct);
        List<GroupRef> accessible;
        if (await permissions.IsGrantedAsync(IdentityPermissions.Groups.ScopeAll, ct))
        {
            accessible = await Groups.Select(g => new GroupRef(g.Id, g.Code)).ToListAsync(ct);
        }
        else
        {
            var codes = memberships.Where(g => g.Code is not null).Select(g => g.Code!).ToList();
            var tops = codes.Where(c => !codes.Any(other => GroupCodes.IsDescendant(c, other))).ToList();
            var descendants = tops.Count == 0 ? [] : await Groups.Where(UnderAny(tops)).Select(g => new GroupRef(g.Id, g.Code)).ToListAsync(ct);
            accessible = memberships.UnionBy(descendants, g => g.Id).ToList();
        }

        return _access[Actor] = new Access(memberships.Select(g => g.Id).ToList(), accessible.OrderBy(g => g.Code, StringComparer.Ordinal).ToList());
    }

    private async Task<IReadOnlyList<Guid>> StoredAsync(CancellationToken ct)
    {
        var actor = Actor;
        if (!_stored.TryGetValue(actor, out var stored))
        {
            _stored[actor] = stored = await db.Set<GroupSelection>().AsNoTracking().Where(s => s.UserId == user.UserId).Select(s => s.GroupId).ToListAsync(ct);
        }

        return stored;
    }

    /// <summary><c>g.Code.StartsWith(code + ".")</c> for any of <paramref name="codes"/>, each code a parameter.</summary>
    // ponytail: one parameter per top-level membership; a user directly in more than ~2000 separate subtrees exceeds SQL Server's parameter limit
    private static Expression<Func<UserGroup, bool>> UnderAny(IEnumerable<string> codes)
    {
        var group = Expression.Parameter(typeof(UserGroup), "g");
        var code = Expression.Property(group, nameof(UserGroup.Code));
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        var body = codes.Select(c => (Expression)Expression.Call(code, startsWith, Parameter(c + "."))).Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<UserGroup, bool>>(body, group);
    }

    // a closure member: EF sends a parameter instead of a literal
    private static Expression Parameter(string value) => ((Expression<Func<string>>)(() => value)).Body;

    private sealed record GroupRef(Guid Id, string? Code);

    private sealed record Access(IReadOnlyList<Guid> Memberships, IReadOnlyList<GroupRef> Groups)
    {
        public IReadOnlyList<Guid> Ids { get; } = Groups.Select(g => g.Id).ToList();
    }
}
