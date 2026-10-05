using Coworkee.Application.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ODataEntityPermissionAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var entityType = ((ControllerActionDescriptor)context.ActionDescriptor).ControllerTypeInfo.GenericTypeArguments.Single();
        if (services.GetRequiredService<ODataEntityRegistry>().Find(entityType)?.Permission is { } permission
            && !await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(permission, context.HttpContext.RequestAborted))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
