using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat;

/// <summary>Direct messages between the people of an organisation, pushed live over the realtime hub.</summary>
[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeRealtimeModule))]
public sealed class CoworkeeChatModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeChatModule).Assembly);
        services.AddSingleton<IModelContributor, ChatModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, ChatPermissionDefinitions>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var chat = app.MapCoworkeeApi("/api/v1/chat").WithTags("Chat").RequireAuthorization();
        chat.MapGet("/contacts", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetChatContactsQuery(), ct).ToHttpResult());
        chat.MapGet("/conversations/{userId:guid}", (Guid userId, DateTimeOffset? before, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetChatConversationQuery(userId, before), ct).ToHttpResult());
        chat.MapPost("/conversations/{userId:guid}", (Guid userId, SendChatMessageRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SendChatMessageCommand(userId, body.Text), ct).ToHttpResult());
        chat.MapPost("/conversations/{userId:guid}/read", (Guid userId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new MarkChatReadCommand(userId), ct).ToHttpResult());
    }
}
