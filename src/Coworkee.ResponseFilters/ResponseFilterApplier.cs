using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nextended.ResponseFilters;
using Nextended.ResponseFilters.Json;
using Nextended.ResponseFilters.Pipeline;

namespace Coworkee.ResponseFilters;

/// <summary>Shapes a value as the API answers it, also outside an endpoint (AI tools, MCP): what the response filters hide is gone.</summary>
public static class ResponseFilterApplier
{
    public static async Task<object?> ApplyAsync(IServiceProvider services, object value, JsonSerializerOptions? json, CancellationToken cancellationToken)
    {
        if (services.GetService<IResponseFilterPipeline>() is not { } pipeline)
        {
            return value;
        }

        var context = new ResponseFilterContext(services, cancellationToken);
        await pipeline.ProcessAsync(value, context);
        return context.StructuralEdits.HasAny ? JsonStructuralTransformer.Transform(value, context.StructuralEdits, json) : value;
    }
}
