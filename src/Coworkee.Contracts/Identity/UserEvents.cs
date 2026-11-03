namespace Coworkee.Contracts.Identity;

/// <summary>A user's name or avatar changed; avatars and cards of the user reload.</summary>
public static class UserEvents
{
    public const string Topic = "tenant:users";

    public const string Changed = "user.changed";
}
