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
                Detail = string.Join(" ", errors.Values.OfType<string[]>().SelectMany(m => m)),
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
