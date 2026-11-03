using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime.Watch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Coworkee.Realtime;

public static class RealtimeWatchServiceCollectionExtensions
{
    /// <summary>
    /// Watches a set another system writes (rows that never pass the app's SaveChanges, like a hub's shipments): while someone follows "type:X" or
    /// "entity:X:id", its date columns are read every few seconds and every row newer than the last look is sent like a change made in the app, so tables,
    /// detail pages and client entity copies follow it. Following the set needs <paramref name="viewPermission"/>.
    /// </summary>
    /// <example><code>
    /// services.AddRealtimeWatch&lt;CalculatedShipment&gt;(HubPermissions.Shipment.View, watch =&gt; watch
    ///     .CreatedAt(s =&gt; s.DateCreatedUtc).ChangedAt(s =&gt; s.DateUpdatedUtc).Every(TimeSpan.FromSeconds(30)).LookBack(TimeSpan.FromMinutes(5)));
    /// </code></example>
    public static IServiceCollection AddRealtimeWatch<T>(this IServiceCollection services, string viewPermission, Action<RealtimeWatchBuilder<T>> configure)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewPermission);
        var builder = new RealtimeWatchBuilder<T>();
        configure(builder);
        services.AddSingleton<IRealtimeWatch>(new RealtimeWatch<T>(builder));
        services.AddSingleton(new RealtimeEntityRegistration(typeof(T), viewPermission));
        services.TryAddSingleton<RealtimeWatches>();
        services.TryAddSingleton<RealtimeWatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RealtimeWatcher>(provider => provider.GetRequiredService<RealtimeWatcher>()));
        return services;
    }
}
