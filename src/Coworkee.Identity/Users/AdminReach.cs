using System.Linq.Expressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Users;

/// <summary><c>Coworkee:Identity</c>: how far identity administration reaches.</summary>
public sealed class IdentityAdministrationOptions
{
    public const string Section = "Coworkee:Identity";

    /// <summary>
    /// Administrators reach only their groups, the sub-groups and the users who are members of one of them (<see cref="AdminReach"/>), unless they hold
    /// <c>Identity.Groups.ScopeAll</c>, are administrators of the organisation or administer every group. Off (the default): every administrator reaches the
    /// whole organisation, as before.
    /// </summary>
    public bool GroupScopedAdministration { get; set; }
}

/// <summary>
/// The groups and users an administrator reaches with <see cref="IdentityAdministrationOptions.GroupScopedAdministration"/>: the groups he is a member of
/// and their sub-groups (<see cref="IGroupScope.AccessibleAsync"/>, never narrowed by the selection) and the users who are a member of one of them; the
/// whole organisation for the system, administrators, holders of <c>Identity.Groups.ScopeAll</c> and whoever reaches every group. The default group
/// (every new user joins it) neither reaches nor counts: it is no unit of anyone. What is out of reach answers as unknown (404) and is absent from lists.
/// <para>
/// A user is changed (account, roles, grants, groups) only by someone who reaches all his groups (403 <see cref="UserInOtherGroups"/>), so no unit
/// changes what a user does for another; a user joins a group of the caller only when he belongs to no group outside the caller's reach. The top level
/// of the tree, the default group and roles (they reach users everywhere) are changed only with the whole organisation (403 <see cref="AllGroupsRequired"/>).
/// <see cref="AccountGuard.CheckUserAsync"/> applies the user rule; the identity requests apply the others.
/// </para>
/// </summary>
public sealed class AdminReach(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IGroupScope scope, IOptions<IdentityAdministrationOptions> options)
{
    public static readonly Error AllGroupsRequired = Error.Forbidden("identity.all_groups_required",
        "Only an administrator of every group can change the top level of the group tree, the default group or roles.");

    public static readonly Error UserInOtherGroups = Error.Forbidden("identity.user_other_groups",
        "The user also belongs to groups you do not administer; an administrator of all his groups can change him.");

    // keyed by the acting user: a CurrentUserScope inside the same DI scope must not reuse another user's reach
    private readonly Dictionary<(Guid?, Guid?), List<Guid>?> _groups = [];

    /// <summary>The groups the caller administers; null for every group and user of the organisation.</summary>
    public async Task<IReadOnlyList<Guid>?> GroupIdsAsync(CancellationToken ct) => await ListAsync(ct);

    /// <summary>Whether the caller reaches every group and user of the organisation.</summary>
    public async Task<bool> AllAsync(CancellationToken ct) => await ListAsync(ct) is null;

    /// <summary>The users the caller reaches (himself always); null for all.</summary>
    public async Task<Expression<Func<User, bool>>?> UserFilterAsync(CancellationToken ct)
    {
        if (await ListAsync(ct) is not { } groups)
        {
            return null;
        }

        var me = currentUser.UserId;
        return u => u.Id == me || u.Groups.Any(m => EF.Parameter(groups).Contains(m.GroupId));
    }

    public async Task<IQueryable<User>> FilterUsersAsync(IQueryable<User> users, CancellationToken ct) =>
        await UserFilterAsync(ct) is { } filter ? users.Where(filter) : users;

    public async Task<IQueryable<UserGroup>> FilterGroupsAsync(IQueryable<UserGroup> groups, CancellationToken ct) =>
        await ListAsync(ct) is { } ids ? groups.Where(g => EF.Parameter(ids).Contains(g.Id)) : groups;

    /// <summary>True for a group in reach; with the whole organisation for any id (the caller checks that it exists).</summary>
    public async Task<bool> ReachesGroupAsync(Guid groupId, CancellationToken ct) =>
        await ListAsync(ct) is not { } groups || groups.Contains(groupId)
        // a group made after the reach was read (an earlier command of this request) lies below a group in reach
        || (groupId != await scope.DefaultGroupIdAsync(ct) && (await scope.AncestorsAsync(groupId, ct)).Any(a => groups.Contains(a.Id)));

    /// <summary>True for a user in reach; with the whole organisation for any id (the caller checks that it exists).</summary>
    public async Task<bool> ReachesUserAsync(Guid userId, CancellationToken ct) =>
        await ListAsync(ct) is not { } groups || userId == currentUser.UserId
        || await db.Set<UserGroupMember>().AnyAsync(m => m.UserId == userId && EF.Parameter(groups).Contains(m.GroupId), ct);

    /// <summary>
    /// Null when the caller may change the account of <paramref name="userId"/>: he reaches all the user's groups. A user in none of them is not found;
    /// <paramref name="joining"/> lets a user of no group (the default group aside) join one of the caller's groups.
    /// </summary>
    public async Task<Error?> CheckUserAsync(Guid userId, CancellationToken ct, bool joining = false)
    {
        if (await ListAsync(ct) is not { } groups || userId == currentUser.UserId)
        {
            return null;
        }

        var memberships = await MembershipsAsync(userId, ct);
        return memberships.Count == 0 ? (joining ? null : UserErrors.NotFound)
            : !memberships.Any(groups.Contains) ? UserErrors.NotFound
            : memberships.All(groups.Contains) ? null
            : UserInOtherGroups;
    }

    /// <summary>The users of <paramref name="users"/> the caller may name for one of his groups: users he reaches and users of no group outside his reach.</summary>
    public async Task<IQueryable<User>> FilterJoinableAsync(IQueryable<User> users, CancellationToken ct)
    {
        if (await ListAsync(ct) is not { } groups)
        {
            return users;
        }

        var me = currentUser.UserId;
        var defaultId = await scope.DefaultGroupIdAsync(ct);
        return users.Where(u => u.Id == me || u.Groups.Any(m => EF.Parameter(groups).Contains(m.GroupId))
                                || !u.Groups.Any(m => m.GroupId != defaultId && !EF.Parameter(groups).Contains(m.GroupId)));
    }

    /// <summary>Null when no member of <paramref name="groupIds"/> belongs to a group outside the reach: changing what the groups give changes them.</summary>
    public async Task<Error?> CheckMembersAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct)
    {
        if (await ListAsync(ct) is not { } groups || groupIds.Count == 0)
        {
            return null;
        }

        var ids = groupIds.ToList();
        var defaultId = await scope.DefaultGroupIdAsync(ct);
        var shared = await db.Set<UserGroupMember>()
            .Where(m => EF.Parameter(ids).Contains(m.GroupId))
            .AnyAsync(m => db.Set<UserGroupMember>().Any(o => o.UserId == m.UserId && o.GroupId != defaultId && !EF.Parameter(groups).Contains(o.GroupId)), ct);
        return shared ? UserInOtherGroups : null;
    }

    /// <summary>Null when a group may be created under or moved to <paramref name="parentId"/>: a parent in reach (else not found), the top level only with every group.</summary>
    public async Task<Error?> CheckParentAsync(Guid? parentId, CancellationToken ct) =>
        await ListAsync(ct) is null ? null
        : parentId is not { } id ? AllGroupsRequired
        : await ReachesGroupAsync(id, ct) ? null
        : GroupErrors.ParentNotFound;

    /// <summary>Null when the caller reaches the whole organisation: for the default group and roles.</summary>
    public async Task<Error?> CheckAllAsync(CancellationToken ct) => await ListAsync(ct) is null ? null : AllGroupsRequired;

    private async Task<List<Guid>?> ListAsync(CancellationToken ct)
    {
        // the system (ISystemDispatcher, CurrentUserScope.BeginSystem) acts for app code that decided the caller may cause the change
        if (!options.Value.GroupScopedAdministration || CurrentUserScope.Current is SystemActor)
        {
            return null;
        }

        var actor = (currentUser.UserId, currentUser.TenantId);
        if (!_groups.TryGetValue(actor, out var groups))
        {
            _groups[actor] = groups = await LoadAsync(ct);
        }

        return groups;
    }

    private async Task<List<Guid>?> LoadAsync(CancellationToken ct)
    {
        if (await permissions.IsGrantedAsync(IdentityPermissions.Groups.ScopeAll, ct) || await AdminGuard.IsAdminAsync(db, currentUser.UserId, ct))
        {
            return null;
        }

        var defaultId = await scope.DefaultGroupIdAsync(ct);
        var memberships = await scope.MembershipsAsync(ct);
        var accessible = await scope.AccessibleAsync(ct);
        List<Guid> groups;
        if (defaultId is { } shared && memberships.Contains(shared))
        {
            // what only the default group opens is out of reach: the groups under the other memberships
            var nodes = await scope.NodesAsync(accessible, ct);
            var own = nodes.Where(n => n.Id != shared && memberships.Contains(n.Id) && n.Code.Length > 0).Select(n => n.Code).ToList();
            groups = [.. nodes.Where(n => (memberships.Contains(n.Id) && n.Id != shared) || own.Any(code => GroupCodes.IsDescendant(n.Code, code))).Select(n => n.Id)];
        }
        else
        {
            groups = [.. accessible];
        }

        groups.Remove(defaultId ?? Guid.Empty);

        // an administrator of every group (also of an organisation without groups) is one of the whole organisation
        return groups.Count == await db.Set<UserGroup>().CountAsync(g => g.Id != defaultId, ct) ? null : groups;
    }

    private async Task<List<Guid>> MembershipsAsync(Guid userId, CancellationToken ct)
    {
        var defaultId = await scope.DefaultGroupIdAsync(ct);
        return await db.Set<UserGroupMember>().Where(m => m.UserId == userId && m.GroupId != defaultId).Select(m => m.GroupId).ToListAsync(ct);
    }
}
