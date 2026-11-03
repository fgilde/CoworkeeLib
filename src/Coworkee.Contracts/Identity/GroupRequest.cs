namespace Coworkee.Contracts.Identity;

public sealed record GroupRequest(string Name, string? Description, Guid? ParentId = null);
