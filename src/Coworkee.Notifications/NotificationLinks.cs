namespace Coworkee.Notifications;

internal static class NotificationLinks
{
    /// <summary>Server-made links are relative paths; they are made absolute, anything that is not a path or http(s) is dropped.</summary>
    public static string? Absolute(string? link, string? publicAppUrl) => link switch
    {
        null or "" => null,
        _ when link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) =>
            publicAppUrl is { Length: > 0 } root ? root.TrimEnd('/') + link : link,
        _ when Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) => link,
        _ => null,
    };
}
