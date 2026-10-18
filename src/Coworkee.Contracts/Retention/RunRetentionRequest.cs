namespace Coworkee.Contracts.Retention;

/// <summary>Delete now what is older than <see cref="Days"/>.</summary>
public sealed record RunRetentionRequest(int Days);
