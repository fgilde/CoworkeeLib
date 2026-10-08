namespace Coworkee.Client.Blazor.Data.Admin;

/// <summary>A row of the OData set "Groups".</summary>
public sealed record GroupRow(Guid Id, string Name, string? Description, IReadOnlyList<GroupMemberRow> Members, IReadOnlyList<GroupRoleRow> Roles);
