namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>What <see cref="ChatPeopleDialog"/> closes with: the picked people and, for a new group, its name.</summary>
public sealed record ChatPeoplePick(IReadOnlyList<Guid> UserIds, string? Name = null);
