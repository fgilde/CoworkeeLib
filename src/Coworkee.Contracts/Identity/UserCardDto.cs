namespace Coworkee.Contracts.Identity;

/// <summary>
/// What other people of the organisation see of a user: name and, when set, an avatar (load it from /users/{id}/avatar?v={AvatarVersion}); without one,
/// <see cref="GravatarHash"/> is the SHA-256 of the trimmed, lower-case email address that gravatar.com knows the user's Gravatar by.
/// </summary>
public sealed record UserCardDto(Guid Id, string Name, string? AvatarVersion, string? GravatarHash = null);
