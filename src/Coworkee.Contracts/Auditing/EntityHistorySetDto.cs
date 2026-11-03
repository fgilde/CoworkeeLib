namespace Coworkee.Contracts.Auditing;

/// <summary>An entity set with a history the current user may read; <see cref="EntityType"/> is the type name the audit log shows.</summary>
public sealed record EntityHistorySetDto(string EntityType, string EntitySet, bool CanRestore, bool SoftDelete);
