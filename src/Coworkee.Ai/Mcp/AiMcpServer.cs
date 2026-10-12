using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Coworkee.Ai.Mcp;

/// <summary>
/// The tools of <see cref="AiToolRunner"/> over MCP (Streamable HTTP, stateless): every call runs as the calling user, signed in with a token
/// or an API key, through the dispatcher with its permission, validation and audit checks.
/// </summary>
internal static class AiMcpServer
{
    public static void AddCoworkeeMcp(this IServiceCollection services) =>
        services.AddMcpServer(options => options.ServerInfo = new Implementation { Name = "coworkee", Version = "1.0" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithListToolsHandler(async (request, ct) =>
            {
                var runner = request.Services!.GetRequiredService<AiToolRunner>();
                if (!await IsOnAsync(request.Services!, ct))
                {
                    return new ListToolsResult { Tools = [] };
                }

                return new ListToolsResult
                {
                    Tools = [.. (await runner.AvailableAsync(ct)).Select(t => new Tool
                    {
                        Name = t.Name,
                        Description = t.Description,
                        InputSchema = JsonSerializer.SerializeToElement(runner.SchemaOf(t)),
                        Annotations = new ToolAnnotations { ReadOnlyHint = t.ReadOnly, DestructiveHint = t.Destructive, OpenWorldHint = false },
                    })],
                };
            })
            .WithCallToolHandler(async (request, ct) =>
            {
                // MCP clients ask their user before tools that are not read only (see the annotations above)
                if (!await IsOnAsync(request.Services!, ct))
                {
                    return new CallToolResult { Content = [new TextContentBlock { Text = "The MCP server of this app is switched off." }], IsError = true };
                }

                var runner = request.Services!.GetRequiredService<AiToolRunner>();
                var input = JsonSerializer.SerializeToElement(request.Params?.Arguments ?? new Dictionary<string, JsonElement>());
                var outcome = await runner.RunAsync(request.Params?.Name ?? string.Empty, input, AiChannels.Mcp, allowWrites: true, ct);
                return new CallToolResult { Content = [new TextContentBlock { Text = outcome.Output }], IsError = !outcome.Succeeded };
            });

    private static Task<bool> IsOnAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<Settings.ISettingProvider>().GetAsync<bool>(AiSettings.McpEnabled, cancellationToken);

    public static void MapCoworkeeMcp(this WebApplication app, CoworkeeMcpOptions options) =>
        app.MapMcp(options.Path).RequireAuthorization().RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Ai).Add(endpoint =>
        {
            var next = endpoint.RequestDelegate!;
            endpoint.RequestDelegate = context => IsAllowedMcpRequest(context.Request) ? next(context) : Reject(context);
        });

    /// <summary>
    /// JSON bodies only and no foreign browser origins: a cross site form or text/plain post must not reach the tools with the cookie of a
    /// signed-in user. A request signed in with an API key (no ambient credential) may carry another origin, e.g. from a desktop client;
    /// browsers still get no CORS answer, so web pages of other sites cannot read the results.
    /// </summary>
    internal static bool IsAllowedMcpRequest(HttpRequest request)
    {
        if (HttpMethods.IsPost(request.Method) && request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        var origin = request.Headers.Origin.ToString();
        return origin.Length == 0
            || request.HttpContext.User.Identity is { IsAuthenticated: true, AuthenticationType: ApiKeyDefaults.Scheme }
            || (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
