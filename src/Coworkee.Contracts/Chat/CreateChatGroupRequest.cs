namespace Coworkee.Contracts.Chat;

public sealed record CreateChatGroupRequest(string Name, IReadOnlyList<Guid> MemberIds);
