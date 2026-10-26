using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

/// <summary>The direct children of a group (the roots for null), by name. Groups.View or Groups.ScopeAll (the scope picker of an operator).</summary>
public sealed record GetGroupChildren(Guid? ParentId) : IQuery<Result<IReadOnlyList<GroupNodeDto>>>;

/// <summary>Groups whose name contains the search text, each with all its ancestors, in tree order. Groups.View or Groups.ScopeAll.</summary>
public sealed record SearchGroupTree(string Search) : IQuery<Result<IReadOnlyList<GroupNodeDto>>>;

internal sealed class SearchGroupTreeValidator : AbstractValidator<SearchGroupTree>
{
    public SearchGroupTreeValidator() => RuleFor(q => q.Search).MaximumLength(200);
}

internal sealed class GetGroupChildrenHandler(CoworkeeDbContext db, IPermissionChecker permissions) : IHandler<GetGroupChildren, Result<IReadOnlyList<GroupNodeDto>>>
{
    public async Task<Result<IReadOnlyList<GroupNodeDto>>> HandleAsync(GetGroupChildren query, CancellationToken cancellationToken) =>
        await GroupNodes.CanBrowseAsync(permissions, cancellationToken)
            ? await db.Set<UserGroup>().Where(g => g.ParentId == query.ParentId).OrderBy(g => g.Name).ToNodes(db).ToListAsync(cancellationToken)
            : GroupNodes.NoBrowse;
}

internal sealed class SearchGroupTreeHandler(CoworkeeDbContext db, IPermissionChecker permissions) : IHandler<SearchGroupTree, Result<IReadOnlyList<GroupNodeDto>>>
{
    // ponytail: the first 200 matches in tree order, no paging; narrow the search text when it is not enough
    private const int MaxMatches = 200;

    public async Task<Result<IReadOnlyList<GroupNodeDto>>> HandleAsync(SearchGroupTree query, CancellationToken cancellationToken)
    {
        if (!await GroupNodes.CanBrowseAsync(permissions, cancellationToken))
        {
            return GroupNodes.NoBrowse;
        }

        var search = query.Search.Trim().ToLowerInvariant();
        if (search.Length == 0)
        {
            return Array.Empty<GroupNodeDto>();
        }

        var matches = await db.Set<UserGroup>().AsNoTracking().Where(g => g.Code != null && g.Name.ToLower().Contains(search))
            .OrderBy(g => g.Code).Take(MaxMatches).Select(g => g.Code!).ToListAsync(cancellationToken);
        var codes = matches.SelectMany(GroupCodes.Prefixes).Distinct().ToList();
        // up to 3200 prefixes, more than the 2100 parameters of a SQL Server command; the 3200-prefix test guards that EF.Parameter keeps this one parameter
        var nodes = await db.Set<UserGroup>().Where(g => EF.Parameter(codes).Contains(g.Code!)).ToNodes(db).ToListAsync(cancellationToken);
        return nodes.OrderBy(g => g.Code, StringComparer.Ordinal).ToList();
    }
}

internal static class GroupNodes
{
    // the same answer the pipeline gives for a missing [RequiresPermission]
    internal static readonly Error NoBrowse = Error.Forbidden("auth.forbidden", $"Permission '{IdentityPermissions.Groups.View}' or '{IdentityPermissions.Groups.ScopeAll}' is required.");

    /// <summary>The tree routes: group admins, and operators with ScopeAll whose scope picker loads the tree lazily (no member lists).</summary>
    internal static async Task<bool> CanBrowseAsync(IPermissionChecker permissions, CancellationToken cancellationToken) =>
        await permissions.IsGrantedAsync(IdentityPermissions.Groups.View, cancellationToken)
        || await permissions.IsGrantedAsync(IdentityPermissions.Groups.ScopeAll, cancellationToken);

    internal static IQueryable<GroupNodeDto> ToNodes(this IQueryable<UserGroup> groups, CoworkeeDbContext db) => groups.AsNoTracking()
        .Select(g => new GroupNodeDto(g.Id, g.ParentId, g.Code ?? "", g.Name, g.Description,
            db.Set<UserGroup>().Count(c => c.ParentId == g.Id), g.Members.Count));
}
