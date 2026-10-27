using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ODataQueryErrorAttribute : Attribute, IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not BadRequestObjectResult { Value: SerializableError errors })
        {
            return;
        }

        // OData's own errors explain the query; anything else (e.g. EF failing to translate) can quote predicates of row filters, so it is logged only
        var odata = errors.TryGetValue(SerializableErrorKeys.ExceptionTypeKey, out var type) && type is string name && name.StartsWith("Microsoft.OData.", StringComparison.Ordinal);
        if (!odata && type is not null)
        {
            context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger<ODataQueryErrorAttribute>()
                .LogWarning("An OData query failed: {Type}: {Message}", type, errors.GetValueOrDefault(SerializableErrorKeys.ExceptionMessageKey));
        }

        context.Result = new JsonResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The query is not valid.",
            // model state errors come as arrays; never the stack trace
            Detail = odata ? errors.GetValueOrDefault(SerializableErrorKeys.MessageKey) as string
                : string.Join(" ", errors.Values.OfType<string[]>().SelectMany(m => m)) is { Length: > 0 } modelState ? modelState : null,
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentType = "application/problem+json",
        };
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
