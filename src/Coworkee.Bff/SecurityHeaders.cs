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
    // MudExFileDisplay shows files from blob and data URLs in img, video, audio, object (PDF) and iframe; the browser talks only to this host
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
            // set before the endpoint, so answers with an own policy (uploaded files, the loading screen logo) replace it
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = policy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = referrer;
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
