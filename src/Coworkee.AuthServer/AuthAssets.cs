using Microsoft.AspNetCore.Http;

namespace Coworkee.AuthServer;

/// <summary>Style sheet and script of the account pages, embedded so every host serves them without static files.</summary>
public static class AuthAssets
{
    public const string BasePath = "/auth-assets";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.Ordinal) { ["auth.css"] = "text/css", ["auth.js"] = "text/javascript" };

    /// <summary>Changes with every build of the library, so browsers may keep the files for a day.</summary>
    public static string Version { get; } = typeof(AuthAssets).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];

    public static string Url(string file) => $"{BasePath}/{file}?v={Version}";

    internal static IResult Serve(string file, HttpContext context)
    {
        if (!ContentTypes.TryGetValue(file, out var contentType) || typeof(AuthAssets).Assembly.GetManifestResourceStream($"Coworkee.AuthServer.Assets.{file}") is not { } stream)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Stream(stream, contentType);
    }
}
