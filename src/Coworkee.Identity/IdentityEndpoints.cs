using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    public static void Map(WebApplication app)
    {
        var setup = app.MapGroup("/api/v1/setup").WithTags("Setup").AllowAnonymous();
        setup.MapGet("/status", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSetupStatus(), ct).ToHttpResult());
        setup.MapPost("/complete", (CompleteSetupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CompleteSetup(body), ct).ToHttpResult());

        var api = app.MapGroup("/api/v1/identity").WithTags("Identity").RequireAuthorization();
        api.MapGet("/permissions/me", async (IPermissionChecker checker, CancellationToken ct) =>
            TypedResults.Ok(await checker.GetGrantedAsync(ct)));
        MapIdentityApi(api);
    }

    static partial void MapIdentityApi(RouteGroupBuilder api);
}
