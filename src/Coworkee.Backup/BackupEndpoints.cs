using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Backup;

internal static class BackupEndpoints
{
    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var backups = app.MapCoworkeeApi("/api/v1/backups").WithTags("Backups").RequireAuthorization();
        backups.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetBackupsQuery(), ct).ToHttpResult());
        backups.MapPost("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateBackupCommand(), ct).ToHttpResult());
        // a whole database: no size limit, only the system organisation's backup managers get here
        backups.MapPost("/upload", UploadAsync).WithMetadata(new DisableRequestSizeLimitAttribute());
        backups.MapPost("/{id:guid}/restore", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RestoreBackupCommand(id), ct).ToHttpResult());
        backups.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteBackupCommand(id), ct).ToHttpResult());
        backups.MapGet("/{id:guid}/download", DownloadAsync);
    }

    /// <summary>The body is the zip itself, as the download gives it.</summary>
    private static async Task<IResult> UploadAsync(string name, HttpRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.SendAsync(new UploadBackupCommand(name, request.Body), cancellationToken).ToHttpResult();

    private static async Task<IResult> DownloadAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var file = await dispatcher.SendAsync(new OpenBackupQuery(id), cancellationToken);
        return file.IsSuccess ? Results.File(file.Value.Content, "application/zip", file.Value.Name) : file.Error.ToProblem();
    }
}
