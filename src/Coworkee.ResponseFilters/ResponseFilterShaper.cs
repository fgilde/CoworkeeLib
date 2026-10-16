using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Coworkee.ResponseFilters;

internal sealed class ResponseFilterShaper(IServiceProvider services, IOptions<JsonOptions> json) : Application.IResponseShaper
{
    public Task<object?> ShapeAsync(object value, CancellationToken cancellationToken) =>
        ResponseFilterApplier.ApplyAsync(services, value, json.Value.SerializerOptions, cancellationToken);
}
