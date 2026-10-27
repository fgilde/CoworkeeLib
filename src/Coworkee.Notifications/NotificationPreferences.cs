using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications;

/// <summary>A user's choice for one notification type; without a row the type's defaults apply.</summary>
[NotAudited]
public sealed class NotificationPreference : Entity
{
    public Guid UserId { get; set; }

    public required string Type { get; set; }

    public NotificationScope Scope { get; set; }

    public bool InApp { get; set; }

    public bool Email { get; set; }
}

/// <summary>A user follows an entity and gets the notifications about it that his preferences let through as Followed.</summary>
[NotAudited]
public sealed class NotificationFollow : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public string? Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

[Description("Lists the notification types with the user's choice: Off, All or Followed, in the app and by e-mail.")]
public sealed record GetNotificationPreferences : IQuery<Result<IReadOnlyList<NotificationPreferenceDto>>>;

[Description("Sets whether and how the user gets one type of notification.")]
public sealed record SaveNotificationPreference(string Type, SaveNotificationPreferenceRequest Preference) : ICommand<Result<NotificationPreferenceDto>>;

[Description("Lists the entities the user follows, newest first.")]
public sealed record GetFollows : IQuery<Result<IReadOnlyList<FollowDto>>>;

[Description("Whether the user follows an entity and may follow it.")]
public sealed record GetFollowState(string EntityType, Guid EntityId) : IQuery<Result<FollowStateDto>>;

[Description("Follows an entity the user sees: its notifications reach him when his preference is Followed or All.")]
public sealed record FollowEntity(string EntityType, Guid EntityId, string? Title = null) : ICommand<Result<FollowStateDto>>;

[Description("Stops following an entity.")]
public sealed record UnfollowEntity(string EntityType, Guid EntityId) : ICommand<Result<FollowStateDto>>;

internal sealed class SaveNotificationPreferenceValidator : AbstractValidator<SaveNotificationPreference>
{
    public SaveNotificationPreferenceValidator() => RuleFor(c => c.Preference.Scope).IsInEnum();
}

internal sealed class FollowEntityValidator : AbstractValidator<FollowEntity>
{
    public FollowEntityValidator() => RuleFor(c => c.Title).MaximumLength(200);
}

internal sealed class NotificationPreferenceHandlers(
    CoworkeeDbContext db, NotificationTypeOptions types, ICurrentUser currentUser, IServiceProvider services, TimeProvider clock)
    : IHandler<GetNotificationPreferences, Result<IReadOnlyList<NotificationPreferenceDto>>>,
      IHandler<SaveNotificationPreference, Result<NotificationPreferenceDto>>,
      IHandler<GetFollows, Result<IReadOnlyList<FollowDto>>>,
      IHandler<GetFollowState, Result<FollowStateDto>>,
      IHandler<FollowEntity, Result<FollowStateDto>>,
      IHandler<UnfollowEntity, Result<FollowStateDto>>
{
    private static readonly Error UnknownType = Error.NotFound("notifications.type_not_found", "There is no such notification type.");
    private static readonly Error NotFollowable = Error.NotFound("notifications.not_followable", "There is no such entity to follow.");
    private static readonly Error Forbidden = Error.Forbidden("notifications.forbidden", "You may not follow this entity.");

    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Preferences need a signed-in user.");

    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> HandleAsync(GetNotificationPreferences query, CancellationToken cancellationToken)
    {
        var mine = await db.Set<NotificationPreference>().AsNoTracking().Where(p => p.UserId == Me).ToDictionaryAsync(p => p.Type, cancellationToken);
        return types.Types.Values.OrderBy(t => t.Category).ThenBy(t => t.Name).Select(t => ToDto(t, mine.GetValueOrDefault(t.Type))).ToList();
    }

    public async Task<Result<NotificationPreferenceDto>> HandleAsync(SaveNotificationPreference command, CancellationToken cancellationToken)
    {
        if (types.Find(command.Type) is not { } type)
        {
            return UnknownType;
        }

        var preference = await db.Set<NotificationPreference>().SingleOrDefaultAsync(p => p.UserId == Me && p.Type == type.Type, cancellationToken);
        if (preference is null)
        {
            preference = new NotificationPreference { UserId = Me, Type = type.Type };
            db.Add(preference);
        }

        (preference.Scope, preference.InApp, preference.Email) = (command.Preference.Scope, command.Preference.InApp, command.Preference.Email);
        return ToDto(type, preference);
    }

    public async Task<Result<IReadOnlyList<FollowDto>>> HandleAsync(GetFollows query, CancellationToken cancellationToken)
    {
        var follows = await Mine().AsNoTracking().OrderByDescending(f => f.CreatedAt).ToListAsync(cancellationToken);
        return follows.Select(f => new FollowDto(f.Id, f.EntityType, f.EntityId, f.Title, types.FollowTargets.GetValueOrDefault(f.EntityType)?.Link?.Invoke(f.EntityId), f.CreatedAt))
            .ToList();
    }

    public async Task<Result<FollowStateDto>> HandleAsync(GetFollowState query, CancellationToken cancellationToken) =>
        await CheckAsync(query.EntityType, query.EntityId, cancellationToken) is { } error
            ? error
            : new FollowStateDto(await Mine().AnyAsync(f => f.EntityType == query.EntityType && f.EntityId == query.EntityId, cancellationToken), true);

    public async Task<Result<FollowStateDto>> HandleAsync(FollowEntity command, CancellationToken cancellationToken)
    {
        if (await CheckAsync(command.EntityType, command.EntityId, cancellationToken) is { } error)
        {
            return error;
        }

        var follow = await Mine().SingleOrDefaultAsync(f => f.EntityType == command.EntityType && f.EntityId == command.EntityId, cancellationToken);
        if (follow is null)
        {
            db.Add(new NotificationFollow
            {
                TenantId = currentUser.TenantId ?? Guid.Empty,
                UserId = Me,
                EntityType = command.EntityType,
                EntityId = command.EntityId,
                Title = command.Title,
                CreatedAt = clock.GetUtcNow(),
            });
        }
        else if (command.Title is not null)
        {
            follow.Title = command.Title;
        }

        return new FollowStateDto(true, true);
    }

    public async Task<Result<FollowStateDto>> HandleAsync(UnfollowEntity command, CancellationToken cancellationToken)
    {
        // following ends also for entities the user may no longer see
        if (!types.FollowTargets.ContainsKey(command.EntityType))
        {
            return NotFollowable;
        }

        db.RemoveRange(await Mine().Where(f => f.EntityType == command.EntityType && f.EntityId == command.EntityId).ToListAsync(cancellationToken));
        return new FollowStateDto(false, await CheckAsync(command.EntityType, command.EntityId, cancellationToken) is null);
    }

    /// <summary>The entity type is followable and the user sees the entity through its OData set: 403 without the set's permission, 404 for a hidden row.</summary>
    private async Task<Error?> CheckAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (!types.FollowTargets.TryGetValue(entityType, out var target))
        {
            return NotFollowable;
        }

        if (target.VisibleIdsAsync is not { } visibleIds)
        {
            return null;
        }

        return await visibleIds(services, cancellationToken) is not { } ids ? Forbidden
            : await ids.AnyAsync(id => id == entityId, cancellationToken) ? null
            : NotFollowable;
    }

    private IQueryable<NotificationFollow> Mine() => db.Set<NotificationFollow>().Where(f => f.UserId == Me);

    private static NotificationPreferenceDto ToDto(NotificationType type, NotificationPreference? mine) => new(
        type.Type, type.Category, type.Name, mine?.Scope ?? type.Scope, mine?.InApp ?? type.InApp, mine?.Email ?? type.Email, type.Scope, type.InApp, type.Email);
}
