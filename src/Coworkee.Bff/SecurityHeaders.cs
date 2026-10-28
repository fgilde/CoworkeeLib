using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Coworkee.Bff;

public static class SecurityHeaders
{
    internal const string Marker = "coworkee:bff:security-headers";

    // Blazor WebAssembly needs wasm-unsafe-eval, BlazorJS (MudBlazor.Extensions) evaluates script strings, MudBlazor positions with inline styles;
    // MudExFileDisplay shows files from blob and data URLs in img, video, audio, object (PDF) and iframe and loads viewers from FileViewerCdns;
    // img-src https: covers the map tiles. Its inline onerror fallback differs per viewer id, so no hash can allow it: without it only that fallback stays silent
    private static readonly (string Directive, string[] Sources)[] Defaults =
    [
        ("default-src", ["'self'"]),
        ("script-src", ["'self'", "'wasm-unsafe-eval'", "'unsafe-eval'"]),
        ("style-src", ["'self'", "'unsafe-inline'"]),
        ("img-src", ["'self'", "data:", "blob:", "https:"]),
        ("media-src", ["'self'", "data:", "blob:"]),
        ("font-src", ["'self'", "data:"]),
        ("connect-src", ["'self'"]),
        ("frame-src", ["'self'", "data:", "blob:"]),
        ("worker-src", ["'self'", "blob:"]),
        ("object-src", ["'self'", "data:", "blob:"]),
        ("base-uri", ["'self'"]),
        ("form-action", ["'self'"]),
    ];

    // Visual Studio's browser link and hot reload talk to their own localhost ports
    private static readonly string[] DevelopmentConnect = ["http://localhost:*", "ws://localhost:*", "wss://localhost:*"];

    /// <summary>
    /// Sends a Content-Security-Policy for the Blazor WebAssembly app (frame-ancestors from <see cref="BffOptions.FrameAncestors"/>),
    /// <c>X-Content-Type-Options: nosniff</c> and a Referrer-Policy with every answer. Call it before <c>UseStaticFiles</c> and <c>MapCoworkeeBff</c>.
    /// </summary>
    public static WebApplication UseCoworkeeSecurityHeaders(this WebApplication app, Action<BffSecurityHeadersOptions>? configure = null)
    {
        var options = app.Services.GetRequiredService<IOptions<BffOptions>>().Value;
        configure?.Invoke(options.SecurityHeaders);
        var policy = ContentSecurityPolicy(options, app.Environment.IsDevelopment());
        var referrer = options.SecurityHeaders.ReferrerPolicy;
        ((IApplicationBuilder)app).Properties[Marker] = true;
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            // set when the answer starts, so headers the reverse proxy copies from the API do not show up twice
            context.Response.OnStarting(() =>
            {
                headers.XContentTypeOptions = "nosniff";
                // only for answers without an own policy: files from the API keep their sandbox, the loading screen logo its own
                headers.TryAdd("Referrer-Policy", referrer);
                headers.TryAdd("Content-Security-Policy", policy);
                return Task.CompletedTask;
            });
            return next(context);
        });
        return app;
    }

    internal static string ContentSecurityPolicy(BffOptions options, bool development)
    {
        var headers = options.SecurityHeaders;
        if (headers.ContentSecurityPolicy is { Length: > 0 } replaced)
        {
            return replaced;
        }

        var directives = Defaults.ToDictionary(d => d.Directive, d => d.Sources.ToList(), StringComparer.OrdinalIgnoreCase);
        if (development)
        {
            directives["connect-src"].AddRange(DevelopmentConnect);
        }

        foreach (var directive in new[] { "script-src", "style-src", "connect-src" })
        {
            directives[directive].AddRange(headers.FileViewerCdns);
        }

        foreach (var (directive, sources) in headers.Sources)
        {
            directives.TryAdd(directive, []);
            directives[directive].AddRange(sources);
        }

        var parts = directives.Select(d => $"{d.Key} {string.Join(' ', d.Value.Distinct())}").ToList();
        parts.Add(BffExtensions.FrameAncestors(options) ?? "frame-ancestors 'self'");
        return string.Join("; ", parts);
    }
}
