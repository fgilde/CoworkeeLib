namespace Coworkee.Contracts.Identity;

/// <summary>A group; <paramref name="OwnerIds"/> are the members who own it, <paramref name="IsDefault"/> marks the group new users join.</summary>
public sealed record GroupDto(Guid Id, string Name, string? Description, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> RoleIds, Guid? ParentId = null, string? Code = null,
    IReadOnlyList<Guid>? OwnerIds = null, bool IsDefault = false);
