namespace Coworkee.Contracts;

/// <summary>Absolute http(s) addresses; on Linux <c>Uri.TryCreate("/path", UriKind.Absolute)</c> succeeds with a file address, so a plain absolute check lets paths through.</summary>
public static class WebAddress
{
    public static bool IsWeb(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    /// <summary>A web address or a path of the app itself (<c>/media/a.png</c>, not <c>//other.host</c>).</summary>
    public static bool IsWebOrLocalPath(string? value) =>
        IsWeb(value) || value is ['/', not '/' and not '\\', ..] || value == "/";
}
