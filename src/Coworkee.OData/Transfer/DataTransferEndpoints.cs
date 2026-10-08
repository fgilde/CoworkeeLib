using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer;

internal static class DataTransferEndpoints
{
    public static void MapDataTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var data = app.MapCoworkeeApi("/api/v1/data").WithTags("Data").RequireAuthorization();
        data.MapGet("/{entitySet}/export", ExportAsync).Produces(StatusCodes.Status200OK, contentType: ExcelExport.ContentType);
        data.MapPost("/{entitySet}/import", ImportAsync).DisableAntiforgery();
    }

    private static Task<IResult> ExportAsync(string entitySet, HttpContext http)
    {
        var entity = http.RequestServices.GetRequiredService<ODataEntityRegistry>().Entities
            .FirstOrDefault(e => string.Equals(e.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));
        return entity is null
            ? Task.FromResult(Results.NotFound())
            : (Task<IResult>)typeof(ExcelExport).GetMethod(nameof(ExcelExport.WriteAsync))!
                .MakeGenericMethod(entity.EntityType)
                .Invoke(null, [http, entity.EntitySet])!;
    }

    private static async Task<IResult> ImportAsync(string entitySet, IFormFile file, HttpContext http)
    {
        if (http.RequestServices.GetRequiredService<ODataEntityRegistry>().FindImport(entitySet) is not { } import)
        {
            return Results.NotFound();
        }

        await using var stream = file.OpenReadStream();
        return Results.Ok(await ExcelImport.ReadAsync(stream, import, http.RequestServices, http.RequestAborted));
    }
}
