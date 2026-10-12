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
using Coworkee.Ai.Mcp;
using Coworkee.Ai.Sessions;
using Microsoft.Extensions.Options;

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

    /// <summary>The kill switch of the MCP server at runtime; Coworkee:Mcp:Enabled decides whether it is mapped at all.</summary>
    public const string McpEnabled = "Mcp.Enabled";
}

/// <summary>
/// Assistant over the tools of all modules (<see cref="AiToolServiceCollectionExtensions.AddAiTool{TRequest}"/>) with conversations kept per user,
/// the same tools as an MCP server (<see cref="CoworkeeMcpOptions"/>), and an audit of every tool call.
/// </summary>
[DependsOn(typeof(CoworkeeSettingsModule))]
public sealed class CoworkeeAiModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeAiModule).Assembly);
        services.AddSingleton<IModelContributor, AiModelContributor>();
        services.AddSingleton<IModelContributor, AiSessionModel>();
        services.AddSingleton<IPermissionDefinitionContributor, AiPermissionDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, AiSettingDefinitions>();
        services.AddSingleton<AiToolSchemas>();
        services.AddSingleton<AiToolCatalog>();
        services.AddODataEntity<AiToolCall>("AiToolCalls", AiPermissions.Audit);
        services.AddScoped<IODataEntityFilter<AiToolCall>, OData.AiToolCallODataFilter>();
        services.AddScoped<AiToolRunner>();
        services.AddScoped<AiChat>();
        services.AddScoped<AiSessionChat>();
        services.AddDataProtection();
        services.AddSingleton<AiPendingProtector>();
        services.AddSingleton<IAiChatClientFactory, AiChatClientFactory>();
        services.AddScoped<AiConnections>();
        services.AddSingleton<AiConnectionProbe>();
        services.AddScoped<Application.Ai.IAiTextGenerator, AiTextGenerator>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, AiPersonalData>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, AiSessionPersonalData>();
        services.AddHttpClient(AiChat.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(5));
        services.Configure<CoworkeeMcpOptions>(context.Configuration.GetSection(CoworkeeMcpOptions.Section));
        services.AddCoworkeeMcp();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var ai = app.MapCoworkeeApi("/api/v1/ai").WithTags("AI").RequireAuthorization();
        ai.MapGet("/status", async (AiChat chat, CancellationToken ct) => TypedResults.Ok(new AiStatusDto((await chat.ConnectAsync(ct)).IsSuccess)));
        ai.MapGet("/tools", async (AiToolRunner runner, CancellationToken ct) =>
            Results.Ok((await runner.AvailableAsync(ct)).Select(t => new AiToolDto(t.Name, t.Description))));
        ai.MapGet("/connection", (AiConnections connections, CancellationToken ct) => connections.GetStatusAsync(ct));
        ai.MapPost("/connection/models", (AiConnectionDraft body, IDispatcher d, CancellationToken ct) => d.SendAsync(new ListAiModels(body), ct).ToHttpResult())
            .RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Ai);
        ai.MapPost("/connection/test", (AiConnectionDraft body, IDispatcher d, CancellationToken ct) => d.SendAsync(new TestAiConnection(body), ct).ToHttpResult())
            .RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Ai);
        ai.MapGet("/tool-calls", ([AsParameters] PageRequest page, string? channel, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetAiToolCalls(page, channel), ct).ToHttpResult());
        ai.MapGet("/sessions", (string? search, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAiSessions(search), ct).ToHttpResult());
        ai.MapGet("/sessions/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAiSession(id), ct).ToHttpResult());
        ai.MapPost("/sessions", (AiSessionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateAiSession(body.Title), ct).ToHttpResult());
        ai.MapPut("/sessions/{id:guid}", (Guid id, AiSessionRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RenameAiSession(id, body.Title ?? string.Empty), ct).ToHttpResult());
        ai.MapDelete("/sessions/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteAiSession(id), ct).ToHttpResult());

        // the turns call the model: rate limited, answered as a stream of JSON lines (AiStreamEvent)
        var turns = app.MapCoworkeeApi("/api/v1/ai/sessions").WithTags("AI").RequireAuthorization().RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Ai);
        turns.MapPost("/{id:guid}/messages", (Guid id, AiSendRequest body, AiSessionChat chat, CancellationToken ct) => chat.SendAsync(id, body, ct))
            .Produces<AiStreamEvent>(contentType: "application/x-ndjson");
        turns.MapPost("/{id:guid}/confirm", (Guid id, AiConfirmRequest body, AiSessionChat chat, CancellationToken ct) => chat.ConfirmAsync(id, body, ct))
            .Produces<AiStreamEvent>(contentType: "application/x-ndjson");

        if (app.Services.GetRequiredService<IOptions<CoworkeeMcpOptions>>().Value is { Enabled: true } mcp)
        {
            app.MapCoworkeeMcp(mcp);
        }
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
            .Add(AiPermissions.Chat, "Use the assistant and the MCP server")
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
            .Add(AiSettings.MaxToolRounds, "Maximum tool rounds per message", SettingType.Int, Scopes, "10")
            .Add(AiSettings.McpEnabled, "MCP server enabled", SettingType.Bool, Scopes, "true",
                description: "Off: MCP clients get no tools and run nothing, also with valid keys. Needs no AI provider.");
}
