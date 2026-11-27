using Coworkee.Contracts.Notifications;
using Coworkee.OData;

namespace Coworkee.Notifications;

/// <summary>The registered notification types and the entity types users may follow.</summary>
public sealed class NotificationTypeOptions
{
    internal Dictionary<string, NotificationType> Types { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, FollowTarget> FollowTargets { get; } = new(StringComparer.Ordinal);

    /// <summary>A type users can switch off or get by mail; notifications of unregistered types reach their recipients in the app only.</summary>
    public NotificationTypeOptions Add(NotificationType type)
    {
        Types[type.Type] = type;
        return this;
    }

    public NotificationTypeOptions Add(
        string type, string? category, string name, NotificationScope scope = NotificationScope.All, bool inApp = true, bool email = false, TimeSpan? emailDelay = null,
        string? mailTemplate = null, Func<IServiceProvider, CancellationToken, Task<TimeSpan?>>? resolveEmailDelay = null) =>
        Add(new NotificationType(type, category, name, scope, inApp, email, emailDelay) { MailTemplate = mailTemplate, ResolveEmailDelay = resolveEmailDelay });

    /// <summary>
    /// Users may follow existing <typeparamref name="TEntity"/>s they see through the entity's OData set; <paramref name="link"/> leads to one. A notification
    /// without an audience reaches only followers who still see the entity, each checked as himself (permission, row filters, groups).
    /// </summary>
    public NotificationTypeOptions Followable<TEntity>(string entityType, Func<Guid, string>? link = null)
        where TEntity : class
    {
        FollowTargets[entityType] = new FollowTarget(ODataEntityAccess.VisibleIdsAsync<TEntity>, link);
        return this;
    }

    /// <summary>
    /// <b>Unchecked:</b> users may follow any id of <paramref name="entityType"/>, nobody checks that they see it. A notification that names such an entity therefore reaches
    /// neither followers nor <see cref="NotificationMessage.Audience"/> members, with or without an audience (a warning is logged); only
    /// <see cref="NotificationMessage.Recipients"/> are notified. Use
    /// <see cref="Followable{TEntity}"/> for records some users must not see.
    /// </summary>
    public NotificationTypeOptions Followable(string entityType, Func<Guid, string>? link = null)
    {
        FollowTargets[entityType] = new FollowTarget(null, link);
        return this;
    }

    internal NotificationType? Find(string type) => Types.GetValueOrDefault(type);
}
