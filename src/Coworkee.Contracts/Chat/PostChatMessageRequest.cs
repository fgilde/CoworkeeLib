namespace Coworkee.Contracts.Chat;

public sealed record PostChatMessageRequest(string Html, Guid? ReplyToId = null, IReadOnlyList<Guid>? AttachmentIds = null);
