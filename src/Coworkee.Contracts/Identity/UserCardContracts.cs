namespace Coworkee.Contracts.Identity;

/// <summary>What other people of the organisation see of a user: name and, when set, an avatar (load it from /users/{id}/avatar?v={AvatarVersion}).</summary>
public sealed record UserCardDto(Guid Id, string Name, string? AvatarVersion);

public sealed record SetAvatarRequest(string? DataUrl);

/// <summary>A user's name or avatar changed; avatars and cards of the user reload.</summary>
public static class UserEvents
{
    public const string Topic = "tenant:users";

    public const string Changed = "user.changed";
}
