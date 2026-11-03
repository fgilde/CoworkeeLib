namespace Coworkee.Contracts.Identity;

/// <summary>The default group of the tenant, null when there is none.</summary>
public sealed record DefaultGroupDto(Guid? GroupId);
