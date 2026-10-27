using Coworkee.Contracts.Notifications;
using Coworkee.OData;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Notifications;

/// <summary>A kind of notification users choose for themselves: whether they get it (Off, All, Followed) and how (in the app, by e-mail).</summary>
/// <param name="Name">English text for the preferences page, a localization key.</param>
/// <param name="EmailDelay">Mails wait this long; another notification of the same type and entity before that replaces the waiting one (one mail when things settle).</param>
public sealed record NotificationType(
    string Type, string? Category, string Name, NotificationScope Scope = NotificationScope.All, bool InApp = true, bool Email = false, TimeSpan? EmailDelay = null);

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
        string type, string? category, string name, NotificationScope scope = NotificationScope.All, bool inApp = true, bool email = false, TimeSpan? emailDelay = null) =>
        Add(new NotificationType(type, category, name, scope, inApp, email, emailDelay));

    /// <summary>Users may follow existing <typeparamref name="TEntity"/>s they see through the entity's OData set; <paramref name="link"/> leads to one.</summary>
    public NotificationTypeOptions Followable<TEntity>(string entityType, Func<Guid, string>? link = null)
        where TEntity : class
    {
        FollowTargets[entityType] = new FollowTarget(ODataEntityAccess.VisibleIdsAsync<TEntity>, link);
        return this;
    }

    /// <summary>Users may follow any id of <paramref name="entityType"/>; the id is not checked.</summary>
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
