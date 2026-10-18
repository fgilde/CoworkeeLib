namespace Coworkee.Application.Retention;

/// <summary>A soft deleted type another <see cref="IRetentionTarget"/> cleans up.</summary>
public sealed record RetentionOwnedType(Type Type);
