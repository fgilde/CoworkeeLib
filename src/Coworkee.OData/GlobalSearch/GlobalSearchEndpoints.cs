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
            (await searcher.AvailableAsync(ct)).Select(p => new GlobalSearchProviderDto(p.Name, p.Group, p.Icon) { Local = p.Local }).ToList());
        // the search box of a data table that names no fields of its own
        search.MapGet("/fields", SearchFieldsAsync);
        search.MapGet("/stream", (string? q, string? types, int? limit, GlobalSearcher searcher, CancellationToken ct) =>
                new GlobalSearchStream(searcher.StreamAsync(q, Split(types), limit, ct)))
            .Produces<GlobalSearchEvent>(contentType: NdJson);
    }

    private static readonly System.Reflection.MethodInfo PathsMethod = typeof(ODataEntityAccess).GetMethod(nameof(ODataEntityAccess.SearchPathsAsync),
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    /// <summary>The declared search fields of every set the user may read, as OData paths; sets without declared fields are left out.</summary>
    private static async Task<Dictionary<string, IReadOnlyList<string>>> SearchFieldsAsync(HttpContext http, ODataEntityRegistry registry, CancellationToken ct)
    {
        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in registry.Entities)
        {
            if (await ODataEntityAccess.IsGrantedAsync(http.RequestServices, entity.EntityType, ct)
                && await (Task<IReadOnlyList<string>>)PathsMethod.MakeGenericMethod(entity.EntityType).Invoke(null, [http.RequestServices, ct])! is { Count: > 0 } paths)
            {
                fields[entity.EntitySet] = paths;
            }
        }

        return fields;
    }

    private static string[] Split(string? types) => (types ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
