namespace Coworkee.Application.Groups;

public sealed record GroupNode(Guid Id, Guid? ParentId, string Code, string Name);
