using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Files;

internal static class FileEndpoints
{
    // inline content is any type a user uploaded: no scripts, no requests elsewhere, no sniffing
    private const string SandboxPolicy = "sandbox; default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'unsafe-inline'";

    public static void MapFileEndpoints(this IEndpointRouteBuilder app, FilesOptions options)
    {
        var files = app.MapCoworkeeApi("/api/v1/files").WithTags("Files").RequireAuthorization();
        files.MapGet("/folders/content", (Guid? folderId, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetFolderContent(folderId), ct).ToHttpResult());
        files.MapPost("/folders", (CreateFolderRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateFolder(body), ct).ToHttpResult());
        files.MapPut("/folders/{id:guid}/name", (Guid id, RenameRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new RenameFolder(id, body.Name), ct).ToHttpResult());
        files.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetFile(id), ct).ToHttpResult());
        files.MapPut("/{id:guid}/name", (Guid id, RenameRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new RenameFile(id, body.Name), ct).ToHttpResult());
        files.MapPost("/move", (MoveRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new MoveEntries(body), ct).ToHttpResult());
        files.MapPost("/delete", (FileSelectionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteEntries(body), ct).ToHttpResult());
        files.MapPost("/", UploadAsync).WithMetadata(new RequestSizeLimitAttribute(options.MaxFileSize));
        files.MapGet("/{id:guid}/content", ContentAsync);
    }

    /// <summary>The body is the file itself, its Content-Type the file's type; nothing is buffered in memory.</summary>
    private static async Task<IResult> UploadAsync(string name, Guid? folderId, HttpRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        await dispatcher.SendAsync(new UploadFile(folderId, name, request.ContentType, request.Body), cancellationToken).ToHttpResult();

    private static async Task<IResult> ContentAsync(Guid id, bool? download, HttpResponse response, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new OpenFile(id), cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var file = result.Value;
        var inline = download != true;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "private, no-store";
        if (inline)
        {
            response.Headers.ContentSecurityPolicy = SandboxPolicy;
        }

        return Results.File(file.Content, file.ContentType, inline ? null : file.Name, enableRangeProcessing: true);
    }
}
