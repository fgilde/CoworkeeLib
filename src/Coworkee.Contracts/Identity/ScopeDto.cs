namespace Coworkee.Contracts.Identity;

/// <summary>A scope clients may request; <paramref name="Resources"/> become the audiences of the access token.</summary>
public sealed record ScopeDto(Guid Id, string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources, bool BuiltIn = false);
