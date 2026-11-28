namespace Coworkee.Client.Blazor.Data.Admin;

/// <summary>
/// A row of the OData set "Groups"; the set has no members (they are rows of "Users", filtered with <c>Groups/any(m: m/GroupId eq {id})</c>).
/// <paramref name="Locked"/> is not in the set: the Groups page fills it from <see cref="Coworkee.Contracts.Identity.GroupDto.Locked"/>.
/// </summary>
public sealed record GroupRow(Guid Id, string Name, string? Description, IReadOnlyList<GroupRoleRow> Roles, Guid? ParentId = null, string? Code = null, bool IsDefault = false,
    bool Locked = false);
