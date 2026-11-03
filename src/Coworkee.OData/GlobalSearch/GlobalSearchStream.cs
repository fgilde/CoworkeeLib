using System.Text.Json;
using Coworkee.Application;
using Coworkee.Contracts.Search;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.GlobalSearch;

/// <summary>Writes each event as a JSON line and flushes it at once; hits pass the response filters like any answer of the API.</summary>
internal sealed class GlobalSearchStream(IAsyncEnumerable<GlobalSearchEvent> events) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.ContentType = GlobalSearchEndpoints.NdJson;
        response.Headers.CacheControl = "no-cache";
        // proxies such as nginx buffer unless told otherwise
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var shaper = httpContext.RequestServices.GetService<IResponseShaper>();
        var ct = httpContext.RequestAborted;
        try
        {
            await response.StartAsync(ct);
            await foreach (var found in events.WithCancellation(ct))
            {
                // a filtered hit may come back as JSON instead of a GlobalSearchHit
                object? hit = found.Hit is { } raw && shaper is not null ? await shaper.ShapeAsync(raw, ct) : found.Hit;
                await response.WriteAsync(JsonSerializer.Serialize(new { found.Provider, Hit = hit, found.State }, JsonSerializerOptions.Web) + "\n", ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // the client typed on or left
        }
    }
}
