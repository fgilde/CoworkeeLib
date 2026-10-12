using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.ApiKeys;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    static partial void MapApiKeys(RouteGroupBuilder api)
    {
        api.MapGet("/api-keys", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMyApiKeys(), ct).ToHttpResult());
        api.MapGet("/api-keys/scopes", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetApiKeyScopes(), ct).ToHttpResult());
        api.MapPost("/api-keys", (CreateApiKeyRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateApiKey(body), ct).ToHttpResult())
            .RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.ApiKeys);
        api.MapPost("/api-keys/{id:guid}/revoke", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RevokeApiKey(id), ct).ToHttpResult());
    }
}
