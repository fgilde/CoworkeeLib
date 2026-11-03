using Coworkee.Application.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa;

/// <summary>Authenticates requests to the Elsa API itself (the app authenticates later in the pipeline) and lets only users with the permission through.</summary>
internal static class ElsaGate
{
    public static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (await context.AuthenticateAsync() is not { Succeeded: true } result)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.User = result.Principal;
        if (!await context.RequestServices.GetRequiredService<IPermissionChecker>().IsGrantedAsync(ElsaPermissions.Manage, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next();
    }
}
