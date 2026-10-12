using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace Coworkee.Bff;

/// <summary>
/// MCP clients reach the API's MCP server under the app's address with their API key. Only the key and the MCP headers go along: never the
/// browser session, its cookies or its token, and no path that could step out of the MCP route.
/// </summary>
internal static class McpForwarding
{
    private static readonly string[] Allowed = ["Accept", "X-Api-Key", "Mcp-Session-Id", "Mcp-Protocol-Version", "Last-Event-ID", "User-Agent"];

    public static void MapMcpForwarder(WebApplication app, string apiAddress, ForwarderRequestConfig config)
    {
        // the same keys as the API's CoworkeeMcpOptions
        if (!app.Configuration.GetValue("Coworkee:Mcp:Enabled", true))
        {
            return;
        }

        var path = app.Configuration["Coworkee:Mcp:Path"] is { Length: > 1 } configured && configured[0] == '/' ? configured.TrimEnd('/') : "/mcp";
        app.MapForwarder(path + "/{**catch-all}", apiAddress, config, transforms =>
        {
            transforms.CopyRequestHeaders = false;
            transforms.AddRequestTransform(transform =>
            {
                var request = transform.HttpContext.Request;
                foreach (var name in Allowed)
                {
                    if (request.Headers.TryGetValue(name, out var value) && value.ToString().Length > 0)
                    {
                        transform.ProxyRequest.Headers.TryAddWithoutValidation(name, value.ToArray());
                    }
                }

                if (request.Headers.Authorization.ToString() is var authorization && authorization.StartsWith("Bearer " + Contracts.Identity.ApiKeyDefaults.Prefix, StringComparison.Ordinal))
                {
                    transform.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", authorization);
                }

                if (request.ContentType is { Length: > 0 } contentType)
                {
                    transform.ProxyRequest.Content?.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }

                return ValueTask.CompletedTask;
            });
        }).Add(endpoint =>
        {
            var next = endpoint.RequestDelegate!;
            endpoint.RequestDelegate = context => IsPlainPath(context) ? next(context) : Reject(context);
        });
    }

    // dot segments, backslashes and encoded slashes or dots could lead the forwarded path out of the MCP route
    internal static bool IsPlainPath(HttpContext context)
    {
        var raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? context.Request.Path.Value ?? string.Empty;
        var path = context.Request.Path.Value ?? string.Empty;
        return !raw.Contains('\\') && !path.Contains('\\')
            && !raw.Contains("%2e", StringComparison.OrdinalIgnoreCase) && !raw.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            && !raw.Contains("%5c", StringComparison.OrdinalIgnoreCase)
            && !path.Split('/').Any(segment => segment is "." or "..")
            && !raw.Split('?')[0].Split('/').Any(segment => segment is "." or "..");
    }

    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    }
}
