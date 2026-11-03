namespace Coworkee.Contracts.Identity;

/// <summary>What other people of the organisation see of a user: name and, when set, an avatar (load it from /users/{id}/avatar?v={AvatarVersion}).</summary>
public sealed record UserCardDto(Guid Id, string Name, string? AvatarVersion);
