using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Notifications;

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
