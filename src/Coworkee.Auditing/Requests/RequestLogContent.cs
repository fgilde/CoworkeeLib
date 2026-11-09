namespace Coworkee.Auditing.Requests;

/// <summary>The bodies the request log keeps: JSON and forms; files, multipart, streams (NDJSON, server-sent events) and binaries never.</summary>
internal static class RequestLogContent
{
    public static bool IsCaptured(string? contentType) => contentType is not null && (IsJson(contentType) || IsForm(contentType));

    public static bool IsForm(string contentType) => contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

    private static bool IsJson(string contentType)
    {
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}
