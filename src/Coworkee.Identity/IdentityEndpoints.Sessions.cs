using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    // the admin list reads the UserSessions OData set
    private static void MapSessions(RouteGroupBuilder api)
    {
        api.MapGet("/sessions/me", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMySessions(), ct).ToHttpResult());
        api.MapPost("/sessions/{id:guid}/end", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new EndSession(id), ct).ToHttpResult());
        api.MapPost("/sessions/counts", (IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSessionCounts(body.Ids), ct).ToHttpResult());
        api.MapGet("/users/{id:guid}/sessions", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUserSessions(id), ct).ToHttpResult());
        api.MapPost("/users/{id:guid}/sessions/end", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new EndUserSessions(id), ct).ToHttpResult());
        api.MapPut("/users/{id:guid}/session-limit", (Guid id, SessionLimitRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetSessionLimit(id, body.MaxSessions), ct).ToHttpResult());
    }
}
