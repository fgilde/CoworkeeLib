using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

/// <summary>
/// Streams user uploaded content with range support: inline under a sandbox policy (no scripts, no requests elsewhere) or as a download
/// with its file name; never sniffed, never cached.
/// </summary>
public sealed class BlobContentResult(Stream content, string contentType, string fileName, bool download) : IResult
{
    public const string SandboxPolicy = "sandbox; default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'unsafe-inline'";

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var headers = httpContext.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "private, no-store";
        if (!download)
        {
            headers.ContentSecurityPolicy = SandboxPolicy;
        }

        return Results.File(content, contentType, download ? fileName : null, enableRangeProcessing: true).ExecuteAsync(httpContext);
    }
}
