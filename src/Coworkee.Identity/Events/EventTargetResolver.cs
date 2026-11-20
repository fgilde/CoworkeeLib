using System.Linq.Expressions;
using System.Security.Claims;
using Coworkee.Application.Authorization;
using Coworkee.Application.Events;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Events;

/// <summary>
/// Resolves targets in the current organisation: what needs no database (ids, the current user) in memory, everything else as one query over the users
/// with the markers of <see cref="EventUser"/> as subqueries over roles, grants and groups, with the rules of the permission checker.
/// </summary>
internal sealed class EventTargetResolver(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, IServiceProvider services)
    : IEventTargetResolver
{
    public async Task<IReadOnlyCollection<Guid>> UsersAsync(EventTarget target, IReadOnlyCollection<Guid>? among = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.NeedsDatabase)
        {
            return target.InMemory(currentUser.UserId, among)!;
        }

        var predicate = await EventTargetPredicate.BuildAsync(target, currentUser.UserId, services, cancellationToken);
        var online = Uses(predicate, nameof(EventUser.IsOnline)) ? await OnlineAsync() : [];
        var filter = new Translation(this, online).Translate(predicate);
        var tenantId = currentUser.TenantId;
        var users = db.Set<User>().AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive);
        if (among is not null)
        {
            var ids = among.ToList();
            users = users.Where(u => ids.Contains(u.Id));
        }

        try
        {
            return await users.Where(filter).Select(u => u.Id).ToListAsync(cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("could not be translated", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The event target {target} cannot run in the database: use the members and markers of EventUser only. {exception.Message}", exception);
        }
    }

    public async Task<IReadOnlyCollection<string>> TopicsAsync(EventTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        switch (target)
        {
            case AllTarget:
                return [RealtimeTopics.TenantEvents];
            case GlobalTarget:
                return [RealtimeTopics.GlobalEvents];
            case CurrentTarget:
                return services.GetService<IHttpContextAccessor>()?.HttpContext?.User.FindFirstValue("sid") is { Length: > 0 } sid ? [RealtimeTopics.Session(sid)]
                    : currentUser.UserId is { } me ? [RealtimeTopics.User(me)]
                    : [];
        }

        // online users first: a big organisation costs what its online users cost
        var online = await OnlineAsync();
        return online.Count == 0 ? [] : [.. (await UsersAsync(target, online, cancellationToken)).Select(RealtimeTopics.User)];
    }

    private async Task<IReadOnlyList<Guid>> OnlineAsync() =>
        services.GetService<RealtimePresence>() is { } presence ? await presence.OnlineAsync(currentUser.TenantId) : [];

    private static bool Uses(Expression expression, string marker)
    {
        var finder = new MarkerFinder(marker);
        finder.Visit(expression);
        return finder.Found;
    }

    private sealed class MarkerFinder(string marker) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Found |= node.Method.DeclaringType == typeof(EventUser) && node.Method.Name == marker;
            return base.VisitMethodCall(node);
        }
    }

    /// <summary>The predicate over <see cref="EventUser"/> as one over <see cref="User"/>: members map by name, markers become subqueries.</summary>
    private sealed class Translation(EventTargetResolver resolver, IReadOnlyList<Guid> online) : ExpressionVisitor
    {
        private static readonly string[] Members = [nameof(EventUser.Id), nameof(EventUser.UserName), nameof(EventUser.Email), nameof(EventUser.FirstName), nameof(EventUser.LastName)];
        private readonly ParameterExpression _user = Expression.Parameter(typeof(User), "u");
        private ParameterExpression _from = null!;

        public Expression<Func<User, bool>> Translate(Expression<Func<EventUser, bool>> predicate)
        {
            _from = predicate.Parameters[0];
            return Expression.Lambda<Func<User, bool>>(Visit(predicate.Body), _user);
        }

        protected override Expression VisitMember(MemberExpression node) =>
            node.Expression == _from
                ? Members.Contains(node.Member.Name) ? Expression.Property(_user, node.Member.Name) : throw Unsupported(node.Member.Name)
                : base.VisitMember(node);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Object != _from)
            {
                return base.VisitMethodCall(node);
            }

            if (node.Method.DeclaringType != typeof(EventUser))
            {
                throw Unsupported(node.Method.Name);
            }

            var arguments = node.Arguments.Select(a => Value(a, node.Method.Name)).ToArray();
            var subquery = node.Method.Name switch
            {
                nameof(EventUser.HasPermission) => resolver.HasPermission((string)arguments[0]!),
                nameof(EventUser.IsInRole) => resolver.IsInRole((string)arguments[0]!),
                nameof(EventUser.InGroup) => resolver.InGroup((Guid)arguments[0]!),
                nameof(EventUser.IsOnline) => IsOnline(online),
                _ => throw Unsupported(node.Method.Name),
            };
            return new Rebind(subquery.Parameters[0], _user).Visit(subquery.Body);
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? throw Unsupported("the user itself") : base.VisitParameter(node);

        private static Expression<Func<User, bool>> IsOnline(IReadOnlyList<Guid> online)
        {
            var ids = online.ToList();
            return u => ids.Contains(u.Id);
        }

        // an argument is a value of the caller (a constant, a captured variable), never something of the user
        private object? Value(Expression argument, string marker)
        {
            var finder = new ParameterFinder(_from);
            finder.Visit(argument);
            return finder.Found
                ? throw new NotSupportedException($"The argument of EventUser.{marker} must be a value, not something of the user: {argument}.")
                : Expression.Lambda<Func<object?>>(Expression.Convert(argument, typeof(object))).Compile(preferInterpretation: true)();
        }

        private static NotSupportedException Unsupported(string member) =>
            new($"EventUser.{member} cannot run in the database; event targets use {string.Join(", ", Members)}, HasPermission, IsInRole, InGroup and IsOnline.");
    }

    private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= node == parameter;
            return node;
        }
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    // the user's groups as the permission checker sees them: memberships of groups of the organisation (the query filters apply)
    private IQueryable<UserGroupMember> Memberships => db.Set<UserGroupMember>().Where(m => db.Set<UserGroup>().Any(g => g.Id == m.GroupId));

    /// <summary>
    /// As <c>PermissionChecker</c>: a role (direct or of a group the user is a member of) with a grant of the permission or of one implying it, or the
    /// system admin role; a grant of the user or of one of his groups. A group's grant does not reach its sub-groups. Computed permissions, grants on single
    /// resources and the active role choice do not count (as <c>IPermissionChecker.UsersGrantedAsync</c>).
    /// </summary>
    private Expression<Func<User, bool>> HasPermission(string permission)
    {
        if (!definitions.Exists(permission))
        {
            return u => false;
        }

        var names = definitions.All.Select(d => d.Name).Where(n => definitions.Expand([n]).Contains(permission)).ToList();
        var tenantId = currentUser.TenantId;
        var grants = db.Set<PermissionGrant>().Where(g => names.Contains(g.Name) && (g.TenantId == null || g.TenantId == tenantId));
        var admins = db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id);
        var roles = grants.Where(g => g.ProviderType == PermissionProviderType.Role).Select(g => g.ProviderKey).Concat(admins);
        var memberships = Memberships;
        return u => grants.Any(g => (g.ProviderType == PermissionProviderType.User && g.ProviderKey == u.Id)
                                    || (g.ProviderType == PermissionProviderType.Group && memberships.Any(m => m.UserId == u.Id && m.GroupId == g.ProviderKey)))
                    || db.Set<IdentityUserRole<Guid>>().Any(r => r.UserId == u.Id && roles.Contains(r.RoleId))
                    || db.Set<UserGroupRole>().Any(r => roles.Contains(r.RoleId) && memberships.Any(m => m.UserId == u.Id && m.GroupId == r.GroupId));
    }

    private Expression<Func<User, bool>> IsInRole(string role)
    {
        var tenantId = currentUser.TenantId;
        var roles = db.Set<Role>().Where(r => r.Name == role && (r.TenantId == null || r.TenantId == tenantId)).Select(r => r.Id);
        var memberships = Memberships;
        return u => db.Set<IdentityUserRole<Guid>>().Any(r => r.UserId == u.Id && roles.Contains(r.RoleId))
                    || db.Set<UserGroupRole>().Any(r => roles.Contains(r.RoleId) && memberships.Any(m => m.UserId == u.Id && m.GroupId == r.GroupId));
    }

    private Expression<Func<User, bool>> InGroup(Guid groupId)
    {
        var groups = db.Set<UserGroup>();
        return u => db.Set<UserGroupMember>().Any(m => m.UserId == u.Id && groups.Any(g => g.Id == m.GroupId
            && groups.Any(parent => parent.Id == groupId && (g.Id == parent.Id || (parent.Code != null && g.Code!.StartsWith(parent.Code + "."))))));
    }
}
