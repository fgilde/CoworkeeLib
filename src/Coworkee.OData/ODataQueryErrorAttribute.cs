using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Coworkee.OData;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ODataQueryErrorAttribute : Attribute, IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is BadRequestObjectResult { Value: SerializableError errors })
        {
            context.Result = new JsonResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The query is not valid.",
                // [EnableQuery] reports Message and ExceptionMessage as text; model state errors come as arrays; never the stack trace
                Detail = string.Join(" ", errors.Where(e => e.Key is "Message" or "ExceptionMessage" || e.Value is string[])
                    .SelectMany(e => e.Value as string[] ?? [e.Value as string ?? string.Empty])),
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentType = "application/problem+json",
            };
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
