using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Backup;

internal static class BackupEndpoints
{
    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var backups = app.MapCoworkeeApi("/api/v1/backups").WithTags("Backups").RequireAuthorization();
        backups.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetBackupsQuery(), ct).ToHttpResult());
        backups.MapPost("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateBackupCommand(), ct).ToHttpResult());
        backups.MapPost("/{id:guid}/restore", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RestoreBackupCommand(id), ct).ToHttpResult());
        backups.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteBackupCommand(id), ct).ToHttpResult());
        backups.MapGet("/{id:guid}/download", DownloadAsync);
    }

    private static async Task<IResult> DownloadAsync(Guid id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var file = await dispatcher.SendAsync(new OpenBackupQuery(id), cancellationToken);
        return file.IsSuccess ? Results.File(file.Value.Content, "application/zip", file.Value.Name) : file.Error.ToProblem();
    }
}
