namespace Coworkee.Contracts.Chat;

public sealed record ChatMembersRequest(IReadOnlyList<Guid> UserIds);
