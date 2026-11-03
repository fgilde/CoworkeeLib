namespace Coworkee.Contracts.Identity;

/// <summary>A group of the current user's picker: <paramref name="IsMember"/> for direct memberships, <paramref name="Selected"/> when it is part of the selection.</summary>
public sealed record MyGroupDto(Guid Id, Guid? ParentId, string Code, string Name, bool IsMember, bool Selected);
