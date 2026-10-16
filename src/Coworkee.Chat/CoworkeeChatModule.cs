using Coworkee.Application.Authorization;
using Coworkee.Application.GlobalSearch;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Coworkee.Settings;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat;

/// <summary>
/// Direct and group chats of the people of an organisation: formatted messages with mentions of people and records, replies, reactions and files,
/// pushed live over the realtime hub. Mentions notify through the notifications module when it is there.
/// </summary>
[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeRealtimeModule), typeof(CoworkeeStorageModule))]
public sealed class CoworkeeChatModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeChatModule).Assembly);
        services.AddScoped<ChatViews>();
        services.AddScoped<ChatBroadcast>();
        services.AddScoped<ChatEntityAccess>();
        services.AddScoped<ChatConversationHandlers>();
        services.AddScoped<ChatMessageHandlers>();
        services.AddSingleton<IModelContributor, ChatModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, ChatPermissionDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, ChatSettingDefinitions>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, ChatPersonalData>();
        services.AddGlobalSearchProvider<ChatSearchProvider>();
        services.AddHostedService<ChatLegacyMessages>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var chat = app.MapCoworkeeApi("/api/v1/chat").WithTags("Chat").RequireAuthorization();
        chat.MapGet("/contacts", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetChatContactsQuery(), ct).ToHttpResult());
        chat.MapGet("/unread-count", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetChatUnreadCountQuery(), ct).ToHttpResult());
        chat.MapGet("/conversations/{userId:guid}", (Guid userId, DateTimeOffset? before, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetChatConversationQuery(userId, before), ct).ToHttpResult());
        chat.MapPost("/conversations/{userId:guid}", (Guid userId, SendChatMessageRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SendChatMessageCommand(userId, body.Text), ct).ToHttpResult());
        chat.MapPost("/conversations/{userId:guid}/read", (Guid userId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new MarkChatReadCommand(userId), ct).ToHttpResult());

        chat.MapGet("/chats", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetChatsQuery(), ct).ToHttpResult());
        chat.MapPost("/chats", (CreateChatGroupRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new CreateChatGroupCommand(body.Name, body.MemberIds), ct).ToHttpResult());
        chat.MapPost("/chats/direct/{userId:guid}", (Guid userId, IDispatcher d, CancellationToken ct) => d.SendAsync(new OpenDirectChatCommand(userId), ct).ToHttpResult());
        chat.MapGet("/chats/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetChatQuery(id), ct).ToHttpResult());
        chat.MapPut("/chats/{id:guid}/name", (Guid id, RenameChatRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RenameChatCommand(id, body.Name), ct).ToHttpResult());
        chat.MapPost("/chats/{id:guid}/members", (Guid id, ChatMembersRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new AddChatMembersCommand(id, body.UserIds), ct).ToHttpResult());
        chat.MapDelete("/chats/{id:guid}/members/{userId:guid}", (Guid id, Guid userId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RemoveChatMemberCommand(id, userId), ct).ToHttpResult());
        chat.MapGet("/chats/{id:guid}/messages", (Guid id, DateTimeOffset? before, string? search, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetChatMessagesQuery(id, before, search), ct).ToHttpResult());
        chat.MapPost("/chats/{id:guid}/messages", (Guid id, PostChatMessageRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new PostChatMessageCommand(id, body.Html, body.ReplyToId, body.AttachmentIds), ct).ToHttpResult());
        chat.MapPost("/chats/{id:guid}/read", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new MarkChatReadByIdCommand(id), ct).ToHttpResult());
        chat.MapPost("/chats/{id:guid}/typing", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new ChatTypingCommand(id), ct).ToHttpResult());
        chat.MapPost("/chats/{id:guid}/attachments", UploadAsync);
        chat.MapGet("/attachments/{id:guid}", ContentAsync);

        chat.MapGet("/messages/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetChatMessageQuery(id), ct).ToHttpResult());
        chat.MapPut("/messages/{id:guid}", (Guid id, EditChatMessageRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new EditChatMessageCommand(id, body.Html), ct).ToHttpResult());
        chat.MapDelete("/messages/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteChatMessageCommand(id), ct).ToHttpResult());
        chat.MapPost("/messages/{id:guid}/reactions", (Guid id, ChatReactionRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new ToggleChatReactionCommand(id, body.Emoji), ct).ToHttpResult());
    }

    /// <summary>The body is the file itself, its Content-Type the file's type; the size limit is the setting, checked while reading.</summary>
    private static async Task<IResult> UploadAsync(Guid id, string name, HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        {
            size.MaxRequestBodySize = null;
        }

        return await dispatcher.SendAsync(new UploadChatAttachmentCommand(id, name, context.Request.ContentType, context.Request.Body), cancellationToken).ToHttpResult();
    }

    private static async Task<IResult> ContentAsync(Guid id, bool? download, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new OpenChatAttachmentQuery(id), cancellationToken);
        return result.IsSuccess
            ? new BlobContentResult(result.Value.Content, result.Value.ContentType, result.Value.Name, download == true)
            : result.Error!.ToProblem();
    }
}
