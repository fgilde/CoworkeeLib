using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.ResponseFilters;

internal sealed class ResponseFilterEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var inner = result is INestedHttpResult nested ? nested.Result : result;
        var value = inner switch
        {
            IValueHttpResult valueResult => valueResult.Value,
            IResult => null,
            _ => inner,
        };
        if (value is null)
        {
            return result;
        }

        var http = context.HttpContext;
        var json = http.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions;
        var filtered = await ResponseFilterApplier.ApplyAsync(http.RequestServices, value, json, http.RequestAborted);
        if (ReferenceEquals(filtered, value))
        {
            return result;
        }

        var statusCode = (inner as IStatusCodeHttpResult)?.StatusCode;
        return TypedResults.Json(filtered, json, statusCode: statusCode);
    }
}
