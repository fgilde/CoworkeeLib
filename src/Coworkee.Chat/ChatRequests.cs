using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Lists the people of the organisation to chat with, with the last direct message and the number of unread direct messages.")]
public sealed record GetChatContactsQuery : IQuery<Result<IReadOnlyList<ChatContactDto>>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Counts the chat messages the signed-in user has not read yet.")]
public sealed record GetChatUnreadCountQuery : IQuery<Result<UnreadCountDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Reads the direct chat with one person, newest last; Before pages back to older messages.")]
public sealed record GetChatConversationQuery(Guid UserId, DateTimeOffset? Before = null, int Take = 50) : IQuery<Result<IReadOnlyList<ChatMessageDto>>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Sends a plain text chat message to a person of the organisation.")]
public sealed record SendChatMessageCommand(Guid ToUserId, string Text) : ICommand<Result<ChatMessageDto>>;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record MarkChatReadCommand(Guid UserId) : ICommand<Result>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Lists the direct and group chats of the signed-in user, newest first, with the last message and the number of unread messages.")]
public sealed record GetChatsQuery : IQuery<Result<IReadOnlyList<ChatConversationDto>>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Reads one chat of the signed-in user with its members.")]
public sealed record GetChatQuery(Guid ConversationId) : IQuery<Result<ChatConversationDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Opens the direct chat with a person, creating it on first use.")]
public sealed record OpenDirectChatCommand(Guid UserId) : ICommand<Result<ChatConversationDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Creates a group chat with a name and the people in it; the creator is its owner.")]
public sealed record CreateChatGroupCommand(string Name, IReadOnlyList<Guid> MemberIds) : ICommand<Result<ChatConversationDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Renames a group chat the signed-in user is a member of.")]
public sealed record RenameChatCommand(Guid ConversationId, string Name) : ICommand<Result<ChatConversationDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Adds people to a group chat the signed-in user is a member of.")]
public sealed record AddChatMembersCommand(Guid ConversationId, IReadOnlyList<Guid> UserIds) : ICommand<Result<ChatConversationDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Removes a person from a group chat: anyone may leave, only the owner removes others.")]
public sealed record RemoveChatMemberCommand(Guid ConversationId, Guid UserId) : ICommand<Result>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Reads the messages of a chat, newest last; Before pages back, Search keeps the messages containing every word.")]
public sealed record GetChatMessagesQuery(Guid ConversationId, DateTimeOffset? Before = null, string? Search = null, int Take = 50) : IQuery<Result<IReadOnlyList<ChatMessageDto>>>;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record GetChatMessageQuery(Guid MessageId) : IQuery<Result<ChatMessageDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Sends a message to a chat: HTML with formatting and mentions (@person, #record), optionally as reply and with uploaded attachments.")]
public sealed record PostChatMessageCommand(Guid ConversationId, string Html, Guid? ReplyToId = null, IReadOnlyList<Guid>? AttachmentIds = null) : ICommand<Result<ChatMessageDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Changes the text of an own chat message; it shows as edited.")]
public sealed record EditChatMessageCommand(Guid MessageId, string Html) : ICommand<Result<ChatMessageDto>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Deletes an own chat message with its attachments; the chat shows that it was deleted.")]
public sealed record DeleteChatMessageCommand(Guid MessageId) : ICommand<Result>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Adds an emoji reaction to a chat message, or removes it when the signed-in user already reacted with it.")]
public sealed record ToggleChatReactionCommand(Guid MessageId, string Emoji) : ICommand<Result<ChatMessageDto>>;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record MarkChatReadByIdCommand(Guid ConversationId) : ICommand<Result>;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record ChatTypingCommand(Guid ConversationId) : ICommand<Result>;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record UploadChatAttachmentCommand(Guid ConversationId, string Name, string? ContentType, Stream Content) : ICommand<Result<ChatAttachmentDto>>;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record OpenChatAttachmentQuery(Guid AttachmentId, bool Thumbnail = false) : IQuery<Result<ChatAttachmentContent>>;

public sealed record ChatAttachmentContent(Stream Content, string ContentType, string Name);

internal sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator() => RuleFor(c => c.Text).NotEmpty().MaximumLength(ChatLimits.TextLength);
}

internal sealed class GetChatConversationValidator : AbstractValidator<GetChatConversationQuery>
{
    public GetChatConversationValidator() => RuleFor(q => q.Take).InclusiveBetween(1, 200);
}

internal sealed class GetChatMessagesValidator : AbstractValidator<GetChatMessagesQuery>
{
    public GetChatMessagesValidator()
    {
        RuleFor(q => q.Take).InclusiveBetween(1, 200);
        RuleFor(q => q.Search).MaximumLength(200);
    }
}

internal sealed class PostChatMessageValidator : AbstractValidator<PostChatMessageCommand>
{
    public PostChatMessageValidator()
    {
        RuleFor(c => c.Html).NotNull().MaximumLength(ChatLimits.HtmlLength);
        RuleFor(c => c.AttachmentIds).Must(ids => ids is null || ids.Count <= ChatLimits.MaxAttachments);
    }
}

internal sealed class EditChatMessageValidator : AbstractValidator<EditChatMessageCommand>
{
    public EditChatMessageValidator() => RuleFor(c => c.Html).NotEmpty().MaximumLength(ChatLimits.HtmlLength);
}

internal sealed class ToggleChatReactionValidator : AbstractValidator<ToggleChatReactionCommand>
{
    public ToggleChatReactionValidator() => RuleFor(c => c.Emoji).NotEmpty().MaximumLength(ChatLimits.EmojiLength).Must(e => e.All(c => c >= 128))
        .WithMessage("Use an emoji.");
}

internal sealed class CreateChatGroupValidator : AbstractValidator<CreateChatGroupCommand>
{
    public CreateChatGroupValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(ChatLimits.NameLength);
        RuleFor(c => c.MemberIds).NotEmpty().Must(ids => ids.Count < ChatLimits.MaxMembers);
    }
}

internal sealed class RenameChatValidator : AbstractValidator<RenameChatCommand>
{
    public RenameChatValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(ChatLimits.NameLength);
}

internal sealed class AddChatMembersValidator : AbstractValidator<AddChatMembersCommand>
{
    public AddChatMembersValidator() => RuleFor(c => c.UserIds).NotEmpty().Must(ids => ids.Count < ChatLimits.MaxMembers);
}
