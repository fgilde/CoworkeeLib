using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nextended.ResponseFilters;
using Nextended.ResponseFilters.Json;
using Nextended.ResponseFilters.Pipeline;

namespace Coworkee.ResponseFilters;

internal sealed class ResponseFilterEndpointFilter(IResponseFilterPipeline pipeline) : IEndpointFilter
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
        var filterContext = new ResponseFilterContext(http.RequestServices, http.RequestAborted);
        await pipeline.ProcessAsync(value, filterContext);
        if (!filterContext.StructuralEdits.HasAny)
        {
            return result;
        }

        var json = http.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions;
        var statusCode = (inner as IStatusCodeHttpResult)?.StatusCode;
        return TypedResults.Json(JsonStructuralTransformer.Transform(value, filterContext.StructuralEdits, json), json, statusCode: statusCode);
    }
}
