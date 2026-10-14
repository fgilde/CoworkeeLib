using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

/// <summary>
/// Streams user uploaded content with range support: inline under a sandbox policy (no scripts, no requests elsewhere) or as a download
/// with its file name; never sniffed, not cached unless the caller says so.
/// </summary>
public sealed class BlobContentResult(Stream content, string contentType, string fileName, bool download, string cacheControl = BlobContentResult.NoCache) : IResult
{
    public const string SandboxPolicy = "sandbox; default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'unsafe-inline'";

    public const string NoCache = "private, no-store";

    /// <summary>For public files: browsers keep them a few minutes, so a file made private again disappears soon after.</summary>
    public const string PublicCache = "public, max-age=300";

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var headers = httpContext.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = cacheControl;
        if (!download)
        {
            headers.ContentSecurityPolicy = SandboxPolicy;
        }

        return Results.File(content, contentType, download ? fileName : null, enableRangeProcessing: true).ExecuteAsync(httpContext);
    }
}
