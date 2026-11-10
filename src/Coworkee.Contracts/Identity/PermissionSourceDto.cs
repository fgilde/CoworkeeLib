using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Identity;

/// <summary>
/// Where a permission of a user comes from: granted to him (<see cref="PermissionSourceKind.Direct"/>), a role (<see cref="Id"/>, <see cref="Name"/>; with
/// <see cref="GroupId"/> a role he has through that group), a grant to a group of his, or a computed permission. A permission implied by a granted one has the source of that one.
/// </summary>
public sealed record PermissionSourceDto(PermissionSourceKind Kind, Guid? Id = null, string? Name = null, Guid? GroupId = null, string? GroupName = null);

[JsonConverter(typeof(JsonStringEnumConverter<PermissionSourceKind>))]
public enum PermissionSourceKind
{
    Direct,
    Role,
    Group,
    Computed,
}
