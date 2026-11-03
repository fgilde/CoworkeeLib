namespace Coworkee.Contracts.Chat;

public sealed record ChatReplyDto(Guid Id, Guid FromUserId, string Text);
