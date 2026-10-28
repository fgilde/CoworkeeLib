using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.Contracts.Data;
using Coworkee.OData.Transfer.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.OData.Transfer;

internal static class DataTransferEndpoints
{
    public static void MapDataTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var data = app.MapCoworkeeApi("/api/v1/data").WithTags("Data").RequireAuthorization();
        data.MapGet("/{entitySet}/export", (string entitySet, HttpContext http, DataExports exports) => exports.ExportAsync(entitySet, http.Request.QueryString, http.RequestAborted))
            .Produces(StatusCodes.Status200OK, contentType: DataExport.XlsxContentType)
            .Produces<DataTransferJobDto>(StatusCodes.Status202Accepted);
        data.MapGet("/{entitySet}/export/stream", (string entitySet, HttpContext http, DataExports exports) => exports.StreamAsync(entitySet, http))
            .Produces<DataExportProgress>(StatusCodes.Status200OK, "application/x-ndjson");
        data.MapPost("/{entitySet}/export/mail", (string entitySet, MailExportRequest request, DataExports exports, CancellationToken ct) => exports.MailAsync(entitySet, request, ct))
            .Produces<DataTransferJobDto>(StatusCodes.Status202Accepted);
        data.MapGet("/downloads/{token}", (string token, DataExports exports, CancellationToken ct) => exports.DownloadAsync(token, ct)).AllowAnonymous()
            .RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Tokens);

        data.MapGet("/export-schedules", (string? entitySet, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetExportSchedulesQuery(entitySet), ct).ToHttpResult());
        data.MapPost("/export-schedules", (SaveExportScheduleRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveExportScheduleCommand(null, body), ct).ToHttpResult());
        data.MapPut("/export-schedules/{id:guid}", (Guid id, SaveExportScheduleRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SaveExportScheduleCommand(id, body), ct).ToHttpResult());
        data.MapDelete("/export-schedules/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteExportScheduleCommand(id), ct).ToHttpResult());
        data.MapPost("/export-schedules/{id:guid}/run", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RunExportScheduleCommand(id), ct).ToHttpResult());

        data.MapPost("/{entitySet}/import", ImportAsync).DisableAntiforgery();

        data.MapGet("/imports", async (ImportService imports, CancellationToken ct) => TypedResults.Ok(await imports.TargetsAsync(ct)));
        data.MapGet("/{entitySet}/import", (string entitySet, ImportService imports, CancellationToken ct) => imports.TargetAsync(entitySet, ct).ToHttpResult());
        data.MapPost("/{entitySet}/import/file", UploadAsync).DisableAntiforgery();
        data.MapPost("/imports/{id:guid}/sheet", (Guid id, ImportParseOptions options, ImportService imports, CancellationToken ct) => imports.SheetAsync(id, options, ct).ToHttpResult());
        data.MapPost("/imports/{id:guid}/preview", (Guid id, ImportSettings settings, ImportService imports, CancellationToken ct) => imports.PreviewAsync(id, settings, ct).ToHttpResult());
        data.MapPost("/imports/{id:guid}/start", (Guid id, ImportSettings settings, ImportService imports, CancellationToken ct) => imports.StartAsync(id, settings, ct).ToHttpResult());
        data.MapPost("/imports/{id:guid}/undo", (Guid id, ImportService imports, CancellationToken ct) => imports.UndoAsync(id, ct).ToHttpResult());

        data.MapGet("/{entitySet}/import/profiles", (string entitySet, ImportProfiles profiles, CancellationToken ct) => profiles.ListAsync(entitySet, ct).ToHttpResult());
        data.MapPost("/{entitySet}/import/profiles", (string entitySet, SaveImportProfileRequest request, ImportProfiles profiles, CancellationToken ct) =>
            profiles.SaveAsync(entitySet, request, ct).ToHttpResult());
        data.MapDelete("/import-profiles/{id:guid}", (Guid id, ImportProfiles profiles, CancellationToken ct) => profiles.DeleteAsync(id, ct).ToHttpResult());

        data.MapGet("/jobs", async (string? entitySet, DataTransferJobs jobs, CancellationToken ct) => TypedResults.Ok(await jobs.ListAsync(entitySet, ct)));
        data.MapGet("/jobs/{id:guid}", (Guid id, DataTransferJobs jobs, CancellationToken ct) => jobs.GetAsync(id, ct).ToHttpResult());
        data.MapGet("/jobs/{id:guid}/file", DownloadAsync);
        data.MapPost("/jobs/{id:guid}/cancel", (Guid id, DataExports exports, CancellationToken ct) => exports.CancelAsync(id, ct).ToHttpResult());
    }

    private static async Task<IResult> UploadAsync(string entitySet, IFormFile file, ImportService imports, CancellationToken cancellationToken)
    {
        var sheet = await imports.UploadAsync(entitySet, file.FileName, await ReadAsync(file, cancellationToken), cancellationToken);
        return sheet.IsSuccess ? TypedResults.Ok(sheet.Value) : sheet.Error!.ToProblem();
    }

    private static async Task<IResult> DownloadAsync(Guid id, DataTransferJobs jobs, CancellationToken cancellationToken)
    {
        var file = await jobs.FileAsync(id, cancellationToken);
        return file.IsSuccess ? Results.File(file.Value.Content, file.Value.ContentType, file.Value.Name) : file.Error!.ToProblem();
    }

    // the one-step import of earlier versions: columns by field name, new rows only, at once
    private static async Task<IResult> ImportAsync(string entitySet, IFormFile file, ImportService imports, CancellationToken cancellationToken)
    {
        var result = await imports.ImportNowAsync(entitySet, file.FileName, await ReadAsync(file, cancellationToken), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        await using var stream = file.OpenReadStream();
        await stream.CopyToAsync(content, cancellationToken);
        return content.ToArray();
    }
}
