using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Groups;

/// <summary>The groups of the current user's picker: every accessible group; with ScopeAll only memberships, the selection and their ancestors (the rest loads lazily).</summary>
public sealed record GetMyGroups : IQuery<Result<IReadOnlyList<MyGroupDto>>>;

/// <summary>Replaces the current user's selection; empty selects nothing (all accessible groups apply).</summary>
public sealed record SelectGroups(IReadOnlyList<Guid> GroupIds) : ICommand<Result>;

internal sealed class SelectGroupsValidator : AbstractValidator<SelectGroups>
{
    public SelectGroupsValidator() => RuleFor(c => c.GroupIds).NotNull();
}

internal sealed class GroupScopeHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IGroupScope scope, IPermissionChecker permissions, IServiceProvider services)
    : IHandler<GetMyGroups, Result<IReadOnlyList<MyGroupDto>>>, IHandler<SelectGroups, Result>
{
    private static readonly Error NotSelectable = Error.Validation(nameof(SelectGroups.GroupIds), "Only groups you can access can be selected.");

    private static readonly Error NoUser = Error.Forbidden("identity.group_selection_no_user", "Only users have a group selection.");

    public async Task<Result<IReadOnlyList<MyGroupDto>>> HandleAsync(GetMyGroups query, CancellationToken cancellationToken)
    {
        var memberships = await scope.MembershipsAsync(cancellationToken);
        var selected = await scope.SelectedAsync(cancellationToken);
        var nodes = await permissions.IsGrantedAsync(IdentityPermissions.Groups.ScopeAll, cancellationToken)
            ? await WithAncestorsAsync(await scope.NodesAsync(memberships.Concat(selected), cancellationToken), cancellationToken)
            : await scope.NodesAsync(await scope.AccessibleAsync(cancellationToken), cancellationToken);
        return nodes.Select(n => new MyGroupDto(n.Id, n.ParentId, n.Code, n.Name, memberships.Contains(n.Id), selected.Contains(n.Id))).ToList();
    }

    public async Task<Result> HandleAsync(SelectGroups command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return NoUser;
        }

        var ids = command.GroupIds.Distinct().ToList();
        if (ids.Except(await scope.AccessibleAsync(cancellationToken)).Any())
        {
            return NotSelectable;
        }

        // selections of one user at once (a browser sends its first one twice) run one after the other: the user's row is locked first, so the later
        // one reads what the earlier one stored; the result is the later selection, never a mix of both or a key clash (L65)
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == me)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ConcurrencyStamp, x => x.ConcurrencyStamp), cancellationToken);
        var stored = await db.Set<GroupSelection>().Where(s => s.UserId == me).ToListAsync(cancellationToken);
        db.RemoveRange(stored.Where(s => !ids.Contains(s.GroupId)));
        db.AddRange(ids.Where(id => stored.All(s => s.GroupId != id)).Select(id => new GroupSelection { UserId = me, GroupId = id }));
        // saved here so other tabs reload only after the commit, as profile changes do
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        if (services.GetService<IRealtimePublisher>() is { } realtime)
        {
            await realtime.PublishAsync(currentUser.TenantId, RealtimeTopics.User(me), GroupSelectionEvents.Changed, new { groupIds = ids }, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<IReadOnlyList<GroupNode>> WithAncestorsAsync(IReadOnlyList<GroupNode> nodes, CancellationToken cancellationToken)
    {
        var codes = nodes.Where(n => n.Code.Length > 0).SelectMany(n => GroupCodes.Prefixes(n.Code)).Distinct().ToList();
        if (codes.Count == 0)
        {
            return nodes;
        }

        var tree = await db.Set<UserGroup>().AsNoTracking().Where(g => EF.Parameter(codes).Contains(g.Code!)).Select(GroupScopeService.Node).ToListAsync(cancellationToken);
        return tree.UnionBy(nodes, n => n.Id).OrderBy(n => n.Code, StringComparer.Ordinal).ToList();
    }
}
