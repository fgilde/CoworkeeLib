using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.AuthServer.Clients;

internal static class ClientEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/identity").WithTags("Identity").RequireAuthorization();
        api.MapGet("/clients", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetClients(), ct).ToHttpResult());
        api.MapPost("/clients", (ClientRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateClient(body), ct).ToHttpResult());
        api.MapPut("/clients/{id:guid}", (Guid id, ClientRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateClient(id, body), ct).ToHttpResult());
        api.MapPost("/clients/{id:guid}/secret", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RegenerateClientSecret(id), ct).ToHttpResult());
        api.MapDelete("/clients/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteClient(id), ct).ToHttpResult());
        api.MapGet("/scopes", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetScopes(), ct).ToHttpResult());
        api.MapPost("/scopes", (ScopeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateScope(body), ct).ToHttpResult());
        api.MapPut("/scopes/{id:guid}", (Guid id, ScopeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateScope(id, body), ct).ToHttpResult());
        api.MapDelete("/scopes/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteScope(id), ct).ToHttpResult());
    }
}
