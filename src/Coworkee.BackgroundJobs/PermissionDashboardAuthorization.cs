using Coworkee.Application.Authorization;
using Coworkee.Contracts.Jobs;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.BackgroundJobs;

internal sealed class PermissionDashboardAuthorization : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var result = await httpContext.AuthenticateAsync();
        if (result.Succeeded)
        {
            httpContext.User = result.Principal;
        }

        return result.Succeeded
            && await httpContext.RequestServices.GetRequiredService<IPermissionChecker>().IsGrantedAsync(JobsPermissions.View, httpContext.RequestAborted);
    }
}
