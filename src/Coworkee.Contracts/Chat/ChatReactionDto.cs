namespace Coworkee.Contracts.Chat;

public sealed record ChatReactionDto(string Emoji, IReadOnlyList<Guid> UserIds);
