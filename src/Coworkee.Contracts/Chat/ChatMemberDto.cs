namespace Coworkee.Contracts.Chat;

public sealed record ChatMemberDto(Guid UserId, string Name, DateTimeOffset? LastReadAt, bool IsOwner);
