using System.Text.Json;
using Coworkee.Application;
using Coworkee.Application.GlobalSearch;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.GlobalSearch;

internal static class GlobalSearchEndpoints
{
    public const string NdJson = "application/x-ndjson";

    public static void MapGlobalSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var search = app.MapCoworkeeApi("/api/v1/search").WithTags("Search").RequireAuthorization();
        search.MapGet("/", (string? q, string? types, int? limit, GlobalSearcher searcher, CancellationToken ct) => searcher.SearchAsync(q, Split(types), limit, ct));
        search.MapGet("/providers", async (GlobalSearcher searcher, CancellationToken ct) =>
            (await searcher.AvailableAsync(ct)).Select(p => new GlobalSearchProviderDto(p.Name, p.Group, p.Icon)).ToList());
        search.MapGet("/stream", (string? q, string? types, int? limit, GlobalSearcher searcher, CancellationToken ct) =>
                new GlobalSearchStream(searcher.StreamAsync(q, Split(types), limit, ct)))
            .Produces<GlobalSearchEvent>(contentType: NdJson);
    }

    private static string[] Split(string? types) => (types ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

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
