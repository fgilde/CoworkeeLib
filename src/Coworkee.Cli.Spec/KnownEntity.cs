namespace Coworkee.Cli.Spec;

/// <summary>An entity a reference can point to: one of the spec, or one the app has already (<see cref="Spec"/> is null then).</summary>
public sealed record KnownEntity(string Module, string Name, string Plural, string? Display, EntitySpec? Spec = null);
