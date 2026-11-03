using Coworkee.Application.GlobalSearch;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
