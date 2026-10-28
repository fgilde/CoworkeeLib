using Coworkee.Contracts.Notifications;
using Coworkee.OData;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Notifications;

/// <summary>A kind of notification users choose for themselves: whether they get it (Off, All, Followed) and how (in the app, by e-mail).</summary>
/// <param name="Name">English text for the preferences page, a localization key.</param>
/// <param name="EmailDelay">Mails wait this long; another notification of the same type and entity before that replaces the waiting one (one mail when things settle).</param>
public sealed record NotificationType(
    string Type, string? Category, string Name, NotificationScope Scope = NotificationScope.All, bool InApp = true, bool Email = false, TimeSpan? EmailDelay = null)
{
    /// <summary>
    /// The mail template (Coworkee.Mailing, editable on the mail templates page) of this type's mails; it gets <see cref="NotificationMessage.MailModel"/>
    /// with <c>user</c> and <c>notification</c>. Null, or a name no template has: the generic mail <c>Notifications.Notification</c>.
    /// </summary>
    public string? MailTemplate { get; init; }

    /// <summary>
    /// The mail delay of the sending tenant, asked when a mail is queued (e.g. from a feature: the services are those of the sender's scope).
    /// Null from it, or no resolver: <see cref="EmailDelay"/>.
    /// </summary>
    public Func<IServiceProvider, CancellationToken, Task<TimeSpan?>>? ResolveEmailDelay { get; init; }

    internal async Task<TimeSpan?> EmailDelayAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        ResolveEmailDelay is { } resolve && await resolve(services, cancellationToken) is { } delay ? delay : EmailDelay;
}

/// <param name="VisibleIdsAsync">The ids the current user sees; null when he may not see the type at all. Without it every id may be followed.</param>
internal sealed record FollowTarget(Func<IServiceProvider, CancellationToken, Task<IQueryable<Guid>?>>? VisibleIdsAsync, Func<Guid, string>? Link);

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
    /// <b>Unchecked:</b> users may follow any id of <paramref name="entityType"/>, nobody checks that they see it. Their notifications therefore reach followers only
    /// when the message names an <see cref="NotificationMessage.Audience"/>, limited to it; without one followers of such types get nothing. Use
    /// <see cref="Followable{TEntity}"/> for records some users must not see.
    /// </summary>
    public NotificationTypeOptions Followable(string entityType, Func<Guid, string>? link = null)
    {
        FollowTargets[entityType] = new FollowTarget(null, link);
        return this;
    }

    internal NotificationType? Find(string type) => Types.GetValueOrDefault(type);
}

public static class NotificationTypeServiceCollectionExtensions
{
    /// <summary>
    /// Registers notification types and followable entity types, e.g.
    /// <c>services.AddNotificationTypes(n => n.Add("shipment.eta", "Shipment", "Estimated arrival changed", email: true, emailDelay: TimeSpan.FromMinutes(1)).Followable&lt;Shipment&gt;("Shipments"))</c>;
    /// may be called by several modules.
    /// </summary>
    public static IServiceCollection AddNotificationTypes(this IServiceCollection services, Action<NotificationTypeOptions> configure)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(NotificationTypeOptions))?.ImplementationInstance is not NotificationTypeOptions options)
        {
            options = new NotificationTypeOptions();
            services.AddSingleton(options);
        }

        configure(options);
        return services;
    }
}
