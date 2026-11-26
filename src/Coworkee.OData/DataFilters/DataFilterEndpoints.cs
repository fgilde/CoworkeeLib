using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.DataFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.OData.DataFilters;

internal static class DataFilterEndpoints
{
    public static void MapDataFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapCoworkeeApi("/api/v1/data-filters").WithTags("Data").RequireAuthorization();
        api.MapGet("/", (string? entitySet, DataFilterLayer? layer, Guid? layerKey, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetDataFiltersQuery(entitySet, layer, layerKey), ct).ToHttpResult());
        api.MapPut("/", (SaveDataFilterRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveDataFilterCommand(body), ct).ToHttpResult());
        api.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDataFilterCommand(id), ct).ToHttpResult());
        api.MapGet("/links", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetDataFilterLinksQuery(), ct).ToHttpResult());
        api.MapPut("/links", (SaveDataFilterLinkRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveDataFilterLinkCommand(body), ct).ToHttpResult());
        api.MapDelete("/links/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDataFilterLinkCommand(id), ct).ToHttpResult());
    }
}
