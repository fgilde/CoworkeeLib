using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Notifications;

/// <summary>
/// Who of a set of users still sees a record: each user checked as himself against the set of the <c>Followable&lt;TEntity&gt;</c> registration of the entity type
/// (permission, row filters, data filters, groups). The <see cref="INotifier"/> applies it to followers and audiences; apps use it to check recipients they address themselves.
/// </summary>
public sealed class NotificationAudience(IServiceProvider services, ICurrentUser currentUser)
{
    /// <summary>The users of <paramref name="userIds"/> who see the record, each checked once; none when the entity type has no <c>Followable&lt;TEntity&gt;</c> registration.</summary>
    public async Task<IReadOnlySet<Guid>> SeeingAsync(IReadOnlyCollection<Guid> userIds, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (services.GetService<NotificationTypeOptions>()?.FollowTargets.GetValueOrDefault(entityType)?.VisibleIdsAsync is not { } visibleIds)
        {
            return new HashSet<Guid>();
        }

        // ponytail: one query per user; batch by permission set when entities gather hundreds of followers
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var seeing = new HashSet<Guid>();
        foreach (var userId in userIds.Distinct())
        {
            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, currentUser.TenantId));
            await using var scope = scopes.CreateAsyncScope();
            if (await visibleIds(scope.ServiceProvider, cancellationToken) is { } ids && await ids.AnyAsync(id => id == entityId, cancellationToken))
            {
                seeing.Add(userId);
            }
        }

        return seeing;
    }
}
