namespace Coworkee.Contracts.Identity;

/// <summary>
/// Gives the users of a role the <paramref name="TargetRoleIds"/> as well (copy) or instead (<paramref name="Move"/>: they leave the role);
/// <paramref name="UserIds"/> limits it to some of them, null takes all.
/// </summary>
public sealed record TransferRoleUsersRequest(IReadOnlyList<Guid> TargetRoleIds, bool Move, IReadOnlyList<Guid>? UserIds = null);
