using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Coworkee.OData;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ODataEntityPermissionAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // the sets only read; changes go through the commands of the modules with their rules
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) && !HttpMethods.IsHead(context.HttpContext.Request.Method))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status405MethodNotAllowed);
            return;
        }

        var entityType = ((ControllerActionDescriptor)context.ActionDescriptor).ControllerTypeInfo.GenericTypeArguments[0];
        if (!await ODataEntityAccess.IsGrantedAsync(context.HttpContext.RequestServices, entityType, context.HttpContext.RequestAborted))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
