using Coworkee.Application.Authorization;
using Coworkee.Contracts.Realtime;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime.Watch;

/// <summary>
/// The watched sets (AddRealtimeWatch) for who may follow them, and the switch for who may manage them: off, a watch reads nothing until switched on again.
/// </summary>
internal static class RealtimeWatchEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/watches", async (IServiceProvider services, IPermissionChecker permissions, CancellationToken cancellationToken) =>
        {
            var watches = services.GetService<RealtimeWatches>();
            var manage = await permissions.IsGrantedAsync(RealtimePermissions.ManageWatches, cancellationToken);
            var list = new List<RealtimeWatchDto>();
            foreach (var registration in services.GetServices<RealtimeEntityRegistration>())
            {
                if (watches?.Find(registration.EntityType.Name) is { } state && await permissions.IsGrantedAsync(registration.Permission, cancellationToken))
                {
                    list.Add(new RealtimeWatchDto(registration.EntityType.Name, state.Enabled, state.Watch.Interval, manage));
                }
            }

            return TypedResults.Ok(list);
        });

        api.MapPut("/watches/{entityType}", async (string entityType, SetRealtimeWatchRequest request, IServiceProvider services, IPermissionChecker permissions,
            CancellationToken cancellationToken) =>
        {
            if (!await permissions.IsGrantedAsync(RealtimePermissions.ManageWatches, cancellationToken))
            {
                return Results.Forbid();
            }

            if (services.GetService<RealtimeWatches>()?.Find(entityType) is not { } state)
            {
                return Results.NotFound();
            }

            state.Enabled = request.Enabled;
            return Results.NoContent();
        });
    }
}
