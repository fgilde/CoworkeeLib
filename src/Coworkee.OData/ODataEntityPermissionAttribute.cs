using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Coworkee.OData;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ODataEntityPermissionAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var entityType = ((ControllerActionDescriptor)context.ActionDescriptor).ControllerTypeInfo.GenericTypeArguments.Single();
        if (!await ODataEntityAccess.IsGrantedAsync(context.HttpContext.RequestServices, entityType, context.HttpContext.RequestAborted))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
