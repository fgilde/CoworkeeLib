namespace Coworkee.Contracts.Identity;

/// <summary>Who loses a custom permission when it is deleted: the roles, groups and users (e-mail) it is granted to, in every organisation.</summary>
public sealed record CustomPermissionUsageDto(IReadOnlyList<string> Roles, IReadOnlyList<string> Groups, IReadOnlyList<string> Users);
