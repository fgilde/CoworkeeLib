using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Coworkee.Ai;

public static class AiSettings
{
    public const string Enabled = AiSettingNames.Enabled;
    public const string Provider = AiSettingNames.Provider;
    public const string Model = AiSettingNames.Model;
    public const string Endpoint = AiSettingNames.Endpoint;
    public const string ApiKey = AiSettingNames.ApiKey;
    public const string Headers = AiSettingNames.Headers;
    public const string AuthHeader = AiSettingNames.AuthHeader;
    public const string Temperature = AiSettingNames.Temperature;
    public const string MaxOutputTokens = AiSettingNames.MaxOutputTokens;
    public const string MaxToolRounds = AiSettingNames.MaxToolRounds;
    public const string DefaultModel = "claude-opus-5-5";
}

/// <summary>
/// Assistant chat over the tools of all modules (<see cref="AiToolServiceCollectionExtensions.AddAiTool{TRequest}"/>),
/// the same tools as an MCP server under /mcp, and an audit of every tool call.
/// </summary>
[DependsOn(typeof(CoworkeeSettingsModule))]
public sealed class CoworkeeAiModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeAiModule).Assembly);
        services.AddSingleton<IModelContributor, AiModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, AiPermissionDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, AiSettingDefinitions>();
        services.AddSingleton<AiToolSchemas>();
        services.AddSingleton<AiToolCatalog>();
        services.AddODataEntity<AiToolCall>("AiToolCalls", AiPermissions.Audit);
        services.AddScoped<IODataEntityFilter<AiToolCall>, OData.AiToolCallODataFilter>();
        services.AddScoped<AiToolRunner>();
        services.AddScoped<AiChat>();
        services.AddSingleton<IAiChatClientFactory, AiChatClientFactory>();
        services.AddScoped<AiConnections>();
        services.AddSingleton<AiConnectionProbe>();
        services.AddScoped<Application.Ai.IAiTextGenerator, AiTextGenerator>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, AiPersonalData>();
        services.AddHttpClient(AiChat.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddMcpServer(options => options.ServerInfo = new Implementation { Name = "coworkee", Version = "1.0" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithListToolsHandler(async (request, ct) =>
            {
                var runner = request.Services!.GetRequiredService<AiToolRunner>();
                return new ListToolsResult
                {
                    Tools = [.. (await runner.AvailableAsync(ct)).Select(t => new ModelContextProtocol.Protocol.Tool
                    {
                        Name = t.Name,
                        Description = t.Description,
                        InputSchema = JsonSerializer.SerializeToElement(runner.SchemaOf(t)),
                        Annotations = new ToolAnnotations { ReadOnlyHint = t.ReadOnly, DestructiveHint = !t.ReadOnly, OpenWorldHint = false },
                    })],
                };
            })
            .WithCallToolHandler(async (request, ct) =>
            {
                // MCP clients ask their user before tools that are not read only (see the annotations above)
                var runner = request.Services!.GetRequiredService<AiToolRunner>();
                var input = JsonSerializer.SerializeToElement(request.Params?.Arguments ?? new Dictionary<string, JsonElement>());
                var outcome = await runner.RunAsync(request.Params?.Name ?? string.Empty, input, AiChannels.Mcp, allowWrites: true, ct);
                return new CallToolResult { Content = [new TextContentBlock { Text = outcome.Output }], IsError = !outcome.Succeeded };
            });
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/ai").WithTags("AI").RequireAuthorization().RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Ai);
        api.MapGet("/tools", async (AiToolRunner runner, CancellationToken ct) =>
            Results.Ok((await runner.AvailableAsync(ct)).Select(t => new AiToolDto(t.Name, t.Description))));
        api.MapPost("/chat", (ChatRequest body, AiChat chat, CancellationToken ct) => chat.SendAsync(body, ct).ToHttpResult());
        api.MapGet("/connection", (AiConnections connections, CancellationToken ct) => connections.GetStatusAsync(ct));
        api.MapPost("/connection/models", (AiConnectionDraft body, IDispatcher d, CancellationToken ct) => d.SendAsync(new ListAiModels(body), ct).ToHttpResult());
        api.MapPost("/connection/test", (AiConnectionDraft body, IDispatcher d, CancellationToken ct) => d.SendAsync(new TestAiConnection(body), ct).ToHttpResult());
        api.MapGet("/tool-calls", ([AsParameters] PageRequest page, string? channel, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetAiToolCalls(page, channel), ct).ToHttpResult());
        app.MapMcp("/mcp").RequireAuthorization().RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Ai).Add(endpoint =>
        {
            var next = endpoint.RequestDelegate!;
            endpoint.RequestDelegate = context => IsAllowedMcpRequest(context.Request) ? next(context) : Reject(context);
        });
    }

    /// <summary>
    /// JSON bodies only and no foreign browser origins: a cross site form or text/plain post must not reach the tools
    /// with the cookie of a signed-in user.
    /// </summary>
    internal static bool IsAllowedMcpRequest(HttpRequest request)
    {
        if (HttpMethods.IsPost(request.Method) && request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        var origin = request.Headers.Origin.ToString();
        return origin.Length == 0
            || (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

[Coworkee.Application.Messaging.AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Audit)]
public sealed record GetAiToolCalls(PageRequest Page, string? Channel) : IQuery<Result<PagedResult<AiToolCallDto>>>;

internal sealed class GetAiToolCallsValidator : FluentValidation.AbstractValidator<GetAiToolCalls>
{
    public GetAiToolCallsValidator() => RuleFor(q => q.Page).SetValidator(new Coworkee.Application.Paging.PageRequestValidator());
}

internal sealed class AiToolCallsHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetAiToolCalls, Result<PagedResult<AiToolCallDto>>>
{
    public async Task<Result<PagedResult<AiToolCallDto>>> HandleAsync(GetAiToolCalls query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var calls = db.Set<AiToolCall>().AsNoTracking().Where(c => c.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            calls = calls.Where(c => c.Channel == query.Channel);
        }

        if (!string.IsNullOrWhiteSpace(query.Page.Search))
        {
            var search = query.Page.Search.Trim();
            calls = calls.Where(c => c.Tool.Contains(search));
        }

        var total = await calls.CountAsync(cancellationToken);
        var items = await calls.OrderByDescending(c => c.At)
            .Skip((query.Page.Page - 1) * query.Page.PageSize).Take(query.Page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AiToolCallDto>([.. items.Select(c => AiToolCall.ToDto(c))], total, query.Page.Page, query.Page.PageSize);
    }
}

internal sealed class AiPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(AiPermissions.GroupName, "AI")
            .Add(AiPermissions.Chat, "Use the assistant and MCP tools")
            .Add(AiPermissions.Audit, "View AI tool calls");
}

internal sealed class AiSettingDefinitions : ISettingDefinitionContributor
{
    // global only: tenants must not switch on the assistant, pick a model or raise limits on the host's key
    private static readonly SettingScope[] Scopes = [SettingScope.Global];

    public void Define(SettingDefinitionContext context) =>
        context.Group("Ai", "AI assistant")
            .Add(AiSettings.Enabled, "Assistant enabled", SettingType.Bool, Scopes, "false")
            .Add(AiSettings.Provider, "Provider", SettingType.Choice, Scopes, AiProviders.Anthropic, choices: [.. AiProviders.All])
            .Add(AiSettings.Model, "Model", SettingType.String, Scopes, description: "Empty: the provider's default. Azure OpenAI: the deployment name.")
            .Add(AiSettings.Endpoint, "Endpoint", SettingType.String, Scopes,
                description: "Empty: the provider's address. Azure OpenAI resource, Ollama or LM Studio address, or the base address of an OpenAI compatible server.")
            .Add(AiSettings.ApiKey, "API key", SettingType.Secret, Scopes)
            .Add(AiSettings.Headers, "Extra headers", SettingType.Text, Scopes, description: "One \"Name: value\" per line, for example HTTP-Referer and X-Title for OpenRouter.")
            .Add(AiSettings.AuthHeader, "Key header", SettingType.String, Scopes, description: "Empty: the key goes as bearer token. Otherwise the header that carries it, for example api-key.")
            .Add(AiSettings.Temperature, "Temperature", SettingType.String, Scopes, description: "Empty: the model's default. 0 to 2, lower answers more predictably.")
            .Add(AiSettings.MaxOutputTokens, "Maximum output tokens", SettingType.Int, Scopes, description: "Empty: the provider's default (16000 for Anthropic).")
            .Add(AiSettings.MaxToolRounds, "Maximum tool rounds per message", SettingType.Int, Scopes, "10");
}
