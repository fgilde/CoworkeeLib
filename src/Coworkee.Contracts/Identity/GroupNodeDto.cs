namespace Coworkee.Contracts.Identity;

public sealed record GroupNodeDto(Guid Id, Guid? ParentId, string Code, string Name, string? Description, int ChildCount, int MemberCount);
