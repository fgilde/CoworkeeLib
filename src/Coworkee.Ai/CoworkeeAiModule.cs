using Coworkee.Ai.Mcp;
using Coworkee.Ai.Sessions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Retention;
using Coworkee.Application.Status;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.Contracts;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Ai;

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
        services.AddSystemStatus<AiStatus>();
        services.AddRetentionTarget<AiToolCallRetention>().AddRetentionTarget<Sessions.AiSessionRetention>();
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
        services.AddSingleton<AiClientResults>();
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

        // the browser's answer to a client_query of a running turn: only the user of that turn finds it waiting
        ai.MapPost("/sessions/{id:guid}/client-results", (Guid id, AiClientResult body, AiClientResults results, ICurrentUser user) =>
                AiClientResults.Invalid(body) is { } invalid ? Error.Validation(nameof(AiClientResult.Output), invalid).ToProblem()
                : user.UserId is { } me && results.Complete(me, id, body) ? Results.NoContent()
                : Error.NotFound("ai.client_result_not_waited_for", "No turn waits for this result.").ToProblem())
            .WithMetadata(new RequestSizeLimitAttribute(AiClientResults.MaxOutputLength * 4L));

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
