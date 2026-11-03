using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class SearchGroupTreeHandler(CoworkeeDbContext db, IPermissionChecker permissions, Users.AdminReach reach) : IHandler<SearchGroupTree, Result<IReadOnlyList<GroupNodeDto>>>
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

        // a group-scoped administrator finds the groups of his reach, with their ancestors inside it
        var reached = await reach.FilterGroupsAsync(db.Set<UserGroup>().AsNoTracking(), cancellationToken);
        var matches = await reached.Where(g => g.Code != null && g.Name.ToLower().Contains(search))
            .OrderBy(g => g.Code).Take(MaxMatches).Select(g => g.Code!).ToListAsync(cancellationToken);
        var codes = matches.SelectMany(GroupCodes.Prefixes).Distinct().ToList();
        // up to 3200 prefixes, more than the 2100 parameters of a SQL Server command; the 3200-prefix test guards that EF.Parameter keeps this one parameter
        var nodes = await reached.Where(g => EF.Parameter(codes).Contains(g.Code!)).ToNodes(db).ToListAsync(cancellationToken);
        return nodes.OrderBy(g => g.Code, StringComparer.Ordinal).ToList();
    }
}
